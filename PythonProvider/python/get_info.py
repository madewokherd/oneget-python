import sys
import distutils.sysconfig
import wheel.pep425tags

sys.stdout.write('.'.join(str(x) for x in sys.version_info))
sys.stdout.write('\0')
sys.stdout.write(distutils.sysconfig.get_python_lib())
sys.stdout.write('\0')
supported_tag_strings = []
for tag in wheel.pep425tags.get_supported():
	supported_tag_strings.append('-'.join(tag))
sys.stdout.write('.'.join(supported_tag_strings))
