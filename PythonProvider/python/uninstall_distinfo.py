import base64
import csv
import hashlib
import os
import sys

basepath = os.path.dirname(sys.argv[1])

to_remove = []

to_remove_set = set()

to_keep = set()

record = open(os.path.join(sys.argv[1], 'RECORD'), 'rb')

def record_filenames(record, filter=None):
	for (path, hash, size) in csv.reader(record):
		path = os.path.normcase(os.path.normpath(os.path.join(basepath, path)))
		if filter is not None and path not in filter:
			continue
		try:
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
		except OSError:
			continue
		yield path

try:
	for path in record_filenames(record):
		to_remove.append(path)
		to_remove_set.add(path)
finally:
	record.close()

for dirname in os.listdir(basepath):
	if not dirname.endswith('.dist-info'):
		continue
	distinfo_path = os.path.join(basepath, dirname)
	if os.path.normcase(os.path.normpath(sys.argv[1])) == os.path.normcase(os.path.normpath(distinfo_path)):
		continue
	try:
		record = open(os.path.join(distinfo_path, 'RECORD'), 'rb')
	except OSError:
		continue
	try:
		for path in record_filenames(record, to_remove_set):
			to_remove_set.remove(path)
			to_keep.add(path)
	finally:
		record.close()

for path in to_remove:
	if path in to_keep:
		continue
	os.remove(path)
	dirname = os.path.dirname(path)
	while True:
		try:
			os.rmdir(dirname)
			dirname = os.path.dirname(dirname)
		except OSError:
			break
