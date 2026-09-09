'use strict';
// Observer only: never replaces arguments, return values, buffers or license checks.
// Canon handles only. Async buffers are read only after observed completion.
const handles = new Map();
const pending = new Map();
const hooked = new Set();
let sequence = 0;
const cap = 4096;
function emit(row) { send(Object.assign({time: new Date().toISOString()}, row)); }
function bytes(p, n) {
    if (p.isNull() || !n) return '';
    try { return Array.from(new Uint8Array(p.readByteArray(Math.min(n, cap))))
        .map(x => x.toString(16).padStart(2,'0')).join(' '); }
    catch (_) { return '[buffer unavailable]'; }
}
function uintAt(p) { try { return p.isNull() ? 0 : p.readU32(); } catch (_) { return 0; } }
function hook(name, callbacks) {
    let address = null;
    for (const dll of ['kernel32.dll','kernelbase.dll']) {
        const m = Process.findModuleByName(dll);
        if (m) address = m.findExportByName(name);
        if (address) break;
    }
    if (!address) { emit({event:'hook_missing', api:name}); return; }
    if (hooked.has(address.toString())) return;
    hooked.add(address.toString());
    Interceptor.attach(address, callbacks);
    emit({event:'hook_installed', api:name});
}
for (const suffix of ['W','A']) {
    hook('CreateFile' + suffix, {
        onEnter(args) {
            this.path = '';
            try { this.path = suffix === 'W' ? args[0].readUtf16String() : args[0].readCString(); } catch (_) {}
            this.watch = /vid_04a9|canon/i.test(this.path || '');
            this.access = args[1].toUInt32(); this.share = args[2].toUInt32();
        },
        onLeave(retval) {
            if (!this.watch) return;
            const err = this.lastError;
            const ok = !retval.equals(ptr(-1));
            if (ok) handles.set(retval.toString(), this.path);
            emit({event:'open', api:'CreateFile'+suffix, path:this.path, handle:retval.toString(),
                success:ok, win32:ok ? 0 : err, access:this.access, share:this.share});
        }
    });
}
function start(ctx, args, api) {
    ctx.handle = args[0].toString();
    ctx.watch = handles.has(ctx.handle);
    if (!ctx.watch) return;
    ctx.id = ++sequence; ctx.started = Date.now();
    let input = ptr(0), inputLength = 0;
    if (api === 'DeviceIoControl') {
        ctx.code = args[1].toUInt32(); input=args[2]; inputLength=args[3].toUInt32();
        ctx.output=args[4]; ctx.capacity=args[5].toUInt32(); ctx.count=args[6]; ctx.ov=args[7];
    } else {
        ctx.code = 0; ctx.capacity=args[2].toUInt32(); ctx.count=args[3]; ctx.ov=args[4];
        ctx.output=api==='ReadFile' ? args[1] : ptr(0);
        if (api==='WriteFile') { input=args[1]; inputLength=ctx.capacity; }
    }
    ctx.api=api;
    emit({event:'request', id:ctx.id, api, handle:ctx.handle, ioctl:'0x'+ctx.code.toString(16),
        input_length:inputLength, input_hex:bytes(input,inputLength), output_capacity:ctx.capacity,
        truncated_input:inputLength>cap, overlapped:!ctx.ov.isNull()});
}
function end(ctx, retval) {
    if (!ctx.watch) return;
    const err=ctx.lastError, ok=retval.toInt32()!==0;
    if (!ok && err===997 && !ctx.ov.isNull()) {
        pending.set(ctx.ov.toString(),{id:ctx.id,api:ctx.api,handle:ctx.handle,output:ctx.output,capacity:ctx.capacity,started:ctx.started});
        emit({event:'pending',id:ctx.id,api:ctx.api,win32:997}); return;
    }
    const n=ok ? uintAt(ctx.count) : 0;
    emit({event:'response',id:ctx.id,api:ctx.api,success:ok,win32:ok ? 0 : err,
        returned:n,output_hex:ok ? bytes(ctx.output,Math.min(n,ctx.capacity)):'',
        truncated_output:n>cap,elapsed_ms:Date.now()-ctx.started});
}
for (const api of ['DeviceIoControl','ReadFile','WriteFile']) {
    hook(api,{onEnter(args){start(this,args,api);},onLeave(retval){end(this,retval);}});
}
for (const api of ['GetOverlappedResult','GetOverlappedResultEx']) {
    hook(api,{
        onEnter(args){this.key=args[1].toString();this.count=args[2];},
        onLeave(retval){
            const row=pending.get(this.key); if (!row) return;
            const err=this.lastError, ok=retval.toInt32()!==0;
            if (!ok && [996,997,258].includes(err)) return;
            pending.delete(this.key);
            const n=ok ? uintAt(this.count) : 0;
            emit({event:'response',id:row.id,api:row.api,completion_api:api,success:ok,win32:ok?0:err,
                returned:n,output_hex:ok ? bytes(row.output,Math.min(n,row.capacity)):'',
                truncated_output:n>cap,elapsed_ms:Date.now()-row.started});
        }
    });
}
hook('CloseHandle',{
    onEnter(args){this.handle=args[0].toString();},
    onLeave(retval){
        if (retval.toInt32()===0 || !handles.has(this.handle)) return;
        handles.delete(this.handle);
        for (const [key,row] of pending) if (row.handle===this.handle) {
            emit({event:'completion_unobserved',id:row.id,api:row.api});pending.delete(key);
        }
        emit({event:'close',handle:this.handle});
    }
});
emit({event:'ready',arch:Process.arch,note:'Read-only observer. No argument or return-value modification. IOCP completion is not decoded.'});
