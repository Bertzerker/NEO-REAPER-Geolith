"""MIT. Verify an exported bundle offline without extracting paths or executing content."""
import zipfile,json,hashlib,sys
def verify(path):
    with zipfile.ZipFile(path) as z:
        names=z.namelist()
        assert len(names)==len(set(names)), 'Duplicate entries'
        assert len(names)<=2000 and sum(e.file_size for e in z.infolist())<=128*1024*1024,'Oversized bundle'
        assert all(not n.startswith(('/','\\')) and '..' not in n.replace('\\','/').split('/') for n in names),'Unsafe path'
        expected=json.loads(z.read('hashes.json'))
        assert set(expected)==set(names)-{'hashes.json'},'Unlisted or missing files'
        for name,sha in expected.items(): assert hashlib.sha256(z.read(name)).hexdigest()==sha, 'Hash mismatch: '+name
        c=json.loads(z.read('capture.json'))
        assert len(c['planes'])==3 and c['id'] and c['identity'],'Invalid capture'
        identity=json.loads(z.read('identity.json'));assert identity['identity']==c['identity'],'Identity metadata mismatch'
        for name in names:
            if name.startswith('notes/'):
                note=json.loads(z.read(name));assert note['capture']==c['id'] and note['revision']>=1,'Note evidence mismatch'
        if 'movie.json' in names:
            movie=json.loads(z.read('movie.json'));assert movie['identity']==c['identity'] and movie['id']==c['movie'],'Movie identity mismatch'
            assert all(row['row']==i for i,row in enumerate(movie['rows'])),'Movie row order mismatch'
        print(json.dumps({'verified':True,'files':len(expected),'capture':c['id'],'frame':c['frame'],'identity':c['identity']}))
if __name__=='__main__': verify(sys.argv[1])
