import os
import sys
import platform
import distutils.sysconfig
import wheel.pep425tags
import struct

sys.stdout.write('.'.join(str(x) for x in sys.version_info))
sys.stdout.write('\0')
sys.stdout.write(distutils.sysconfig.get_python_lib())
sys.stdout.write('\0')
sys.stdout.write(str(len(struct.pack('P', 0))))
sys.stdout.write('\0')
supported_tag_strings = []
for tag in wheel.pep425tags.get_supported():
	supported_tag_strings.append('-'.join(tag))
sys.stdout.write('.'.join(supported_tag_strings))

def format_full_version(info):
	version = '{0.major}.{0.minor}.{0.micro}'.format(info)
	kind = info.releaselevel
	if kind != 'final':
		version += kind[0] + str(info.serial)
	return version

# EnvironmentMarkerVariable values
sys.stdout.write('\0')
sys.stdout.write(os.name)
sys.stdout.write('\0')
sys.stdout.write(sys.platform)
sys.stdout.write('\0')
sys.stdout.write(platform.release())
sys.stdout.write('\0')
if sys.version_info >= (3, 3):
	sys.stdout.write(sys.implementation.name)
sys.stdout.write('\0')
sys.stdout.write(platform.machine())
sys.stdout.write('\0')
sys.stdout.write(platform.python_implementation())
sys.stdout.write('\0')
sys.stdout.write(platform.python_version()[:3])
sys.stdout.write('\0')
sys.stdout.write(format_full_version(sys.version_info))
sys.stdout.write('\0')
sys.stdout.write(platform.version())
sys.stdout.write('\0')
if sys.version_info >= (3, 3):
	sys.stdout.write(format_full_version(sys.implementation.version))
