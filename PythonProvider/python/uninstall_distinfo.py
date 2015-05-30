import base64
import csv
import hashlib
import os
import sys

basepath = os.path.dirname(sys.argv[1])

record = open(os.path.join(sys.argv[1], 'RECORD'), 'rb')

to_remove = []

paths_to_remove = []

try:
	for (path, hash, size) in csv.reader(record):
		path = os.path.join(basepath, path)
		if size:
			if os.path.getsize(path) != int(size):
				continue
		if hash:
			alg, hash = hash.split('=', 1)
			h = hashlib.new(alg)
			f = open(path, 'rb')
			try:
				buf = f.read(8192)
				while buf:
					h.update(buf)
					buf = f.read(8192)
			finally:
				f.close()
			actual_hash = base64.urlsafe_b64encode(h.digest()).rstrip('=')
			if actual_hash != hash:
				continue
		to_remove.append(path)
finally:
	record.close()

for path in to_remove:
	os.remove(path)
	dirname = os.path.dirname(path)
	while True:
		try:
			os.rmdir(dirname)
			dirname = os.path.dirname(dirname)
		except OSError:
			break
