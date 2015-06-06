import sys
import wheel.install

whl = wheel.install.WheelFile(sys.argv[1])

whl.install(force=True)
