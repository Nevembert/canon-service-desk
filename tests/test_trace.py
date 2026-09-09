import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('capture',Path(__file__).parents[1]/'trace'/'capture.py')
capture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(capture)

class TraceTests(unittest.TestCase):
    def text(self,rows): return '\n'.join(capture.analyze(rows))
    def test_no_capture_not_no_printer(self):
        self.assertIn('не доказательство отсутствия', self.text([]))
    def test_actual_large_buffer_crc(self):
        rows=[{'event':'request','id':1,'ioctl':'0x220034','output_capacity':5000},
              {'event':'response','id':1,'success':False,'win32':23}]
        self.assertIn('буфером больше 4094',self.text(rows))
    def test_small_buffer_crc_not_same_diagnosis(self):
        rows=[{'event':'request','id':1,'ioctl':'0x220034','output_capacity':1024},
              {'event':'response','id':1,'success':False,'win32':23}]
        self.assertIn('сам по себе не объясняет',self.text(rows))
    def test_pending_not_success(self):
        self.assertIn('без наблюдавшегося завершения',self.text([{'event':'pending','id':4}]))
    def test_pending_with_completion(self):
        rows=[{'event':'pending','id':4},{'event':'response','id':4,'success':True,'win32':0}]
        self.assertNotIn('без наблюдавшегося завершения',self.text(rows))
        self.assertIn('не подтверждает',self.text(rows))
    def test_access_denied(self):
        self.assertIn('отказ в доступе',self.text([{'event':'open','success':False,'win32':5}]))
    def test_observer_error_not_ignored(self):
        self.assertIn('неполным',self.text([{'event':'observer_error'}]))

if __name__=='__main__': unittest.main()
