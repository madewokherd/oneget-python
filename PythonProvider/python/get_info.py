import sys
import distutils.sysconfig

sys.stdout.write('.'.join(str(x) for x in sys.version_info))
sys.stdout.write('\n')
sys.stdout.write(distutils.sysconfig.get_python_lib())
