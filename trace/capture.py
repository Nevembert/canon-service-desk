"""Local Canon Service Tool USB observer. No printer writes are generated here.

The selected Service Tool executes its own operations. Frida logs only its Canon
device I/O. No payment/licensing code, arguments or responses are modified.
"""
from __future__ import annotations
import argparse
from datetime import datetime
import hashlib
import json
import os
from pathlib import Path
import sys
import threading
import zipfile


def analyze(rows: list[dict]) -> list[str]:
    result = []
    requests = {r['id']: r for r in rows if r.get('event') == 'request'}
    opens = [r for r in rows if r.get('event') == 'open']
    responses = [r for r in rows if r.get('event') == 'response']
    if not opens:
        result.append('Открытие Canon не зафиксировано. Возможны другой способ доступа, отсутствие устройства или сбой до открытия. Это не доказательство отсутствия принтера.')
    for r in opens:
        if r.get('success'):
            result.append('Service Tool открыла интерфейс Canon: базовое открытие устройства работает.')
        elif r.get('win32') == 32:
            result.append('Windows 32: конфликт совместного доступа. Закройте PrintHelp и другие программы принтера; повторите запись.')
        elif r.get('win32') == 5:
            result.append('Windows 5: отказ в доступе. Проверьте права запуска и другие процессы, работающие с принтером.')
        else:
            result.append(f"Открытие Canon завершилось ошибкой Windows {r.get('win32')}. Путь и аргументы есть в JSONL.")
    for r in responses:
        q = requests.get(r.get('id'), {})
        if r.get('win32') == 23 and q.get('ioctl') == '0x220034':
            if q.get('output_capacity', 0) > 4094:
                result.append('Зафиксировано: GET_1284_ID с буфером больше 4094 байт получил Windows 23 (CRC). Microsoft рекомендует уменьшить буфер до 4094 или меньше. Наше приложение использует 4094, затем 1024 при CRC. Это конкретный кандидат причины сбоя старой утилиты.')
            else:
                result.append('GET_1284_ID вернул CRC даже при буфере <=4094. Проверяйте драйвер и USB-связь; размер буфера сам по себе не объясняет сбой.')
        elif not r.get('success') and r.get('win32') not in (None, 0):
            result.append(f"Запрос #{r.get('id')} ({q.get('api',r.get('api'))}, {q.get('ioctl','')}) завершился Win32={r.get('win32')}. Команда и ответ сохранены; точная причина требует анализа.")
    if responses and all(r.get('success') for r in responses):
        result.append('У наблюдавшихся завершённых запросов Win32 не сообщил ошибку. Это не подтверждает правильность протокола или успешный сброс: нужно разбирать содержимое ответов.')
    completed = {r.get('id') for r in responses}
    if any(r.get('event') == 'pending' and r.get('id') not in completed for r in rows):
        result.append('Есть асинхронные запросы без наблюдавшегося завершения. Не считайте их успешными; возможен неподдержанный способ получения завершения (например IOCP).')
    if any(r.get('event') == 'observer_error' for r in rows):
        result.append('В наблюдателе возникла ошибка: журнал может быть неполным. Подробности observer_error сохранены.')
    return list(dict.fromkeys(result))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('exe', nargs='?', type=Path)
    parser.add_argument('--analyze', type=Path, help='Analyze an existing JSONL offline; no Frida required')
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    if args.analyze:
        rows = [json.loads(line) for line in args.analyze.read_text(encoding='utf-8-sig').splitlines() if line.strip()]
        print('\n\n'.join(analyze(rows)))
        return 0
    if sys.platform != 'win32':
        print('Захват предназначен для Windows. Анализ существующего файла доступен с --analyze.')
        return 2
    try:
        import frida
    except ImportError:
        print('Не установлена Frida. Сначала запустите setup_trace.cmd.')
        return 2
    exe = args.exe
    if not exe:
        import tkinter as tk
        from tkinter import filedialog
        root = tk.Tk(); root.withdraw()
        selected = filedialog.askopenfilename(title='Выберите уже скачанную Service Tool EXE', filetypes=[('Windows EXE','*.exe')])
        root.destroy()
        if not selected:
            return 0
        exe = Path(selected)
    exe = exe.resolve(strict=True)
    if exe.suffix.lower() != '.exe':
        raise ValueError('Нужен файл .exe')
    base = Path(os.environ.get('LOCALAPPDATA', str(Path.home()))) / 'CanonServiceDesk' / 'Traces'
    folder = args.output or base / datetime.now().strftime('%Y%m%d_%H%M%S_%f')
    folder.mkdir(parents=True, exist_ok=False)
    rows: list[dict] = []
    lock = threading.Lock()
    finished = threading.Event()
    device = frida.get_local_device()
    session = None
    pid = None
    resumed = False
    capture = (folder / 'usb-trace.jsonl').open('w', encoding='utf-8', buffering=1)

    def record(row: dict) -> None:
        with lock:
            rows.append(row)
            capture.write(json.dumps(row, ensure_ascii=False) + '\n')
        print(row.get('event'), row.get('api',''), 'Win32='+str(row['win32']) if 'win32' in row else '')

    def on_message(message, data):
        if message['type'] == 'send':
            record(message['payload'])
        else:
            record({'event':'observer_error','detail':message})

    def detached(*unused):
        finished.set()

    try:
        record({'event':'capture_start','time':datetime.now().isoformat(), 'exe_name':exe.name,
                'sha256':hashlib.sha256(exe.read_bytes()).hexdigest(), 'frida':frida.__version__})
        print('Запускается выбранная Service Tool. Воспроизведите ошибку, затем закройте её окно.')
        print('Наблюдатель ничего не сбрасывает. Нажатые вами кнопки Service Tool выполняют её обычные действия.')
        print('Логи сохраняются локально:', folder)
        pid = device.spawn([str(exe)], cwd=str(exe.parent))
        session = device.attach(pid)
        session.on('detached', detached)
        script = session.create_script(Path(__file__).with_name('usb_observer.js').read_text(encoding='utf-8'))
        script.on('message', on_message)
        script.load()
        device.resume(pid); resumed = True
        while not finished.wait(0.25):
            pass
    except KeyboardInterrupt:
        record({'event':'capture_stopped','note':'Остановлено пользователем; уже запущенная Service Tool продолжает работать.'})
    except Exception as error:
        record({'event':'observer_error','detail':str(error)})
    finally:
        if session:
            try: session.detach()
            except Exception: pass
        if pid is not None and not resumed:
            try: device.kill(pid)
            except Exception: pass
        with lock:
            report = analyze(list(rows))
            capture.close()
        (folder / 'analysis.txt').write_text('\n\n'.join(report), encoding='utf-8-sig')
        archive = folder.with_suffix('.zip')
        with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
            for path in folder.iterdir():
                if path.is_file(): z.write(path, path.name)
        print('\n' + '\n\n'.join(report))
        print('\nАрхив для разбора:', archive)
        print('Может содержать серийный номер и пути USB. Автоматической публикации нет.')
    return 0 if resumed else 1


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except Exception as error:
        print('Ошибка запуска:', error)
        raise SystemExit(1)
