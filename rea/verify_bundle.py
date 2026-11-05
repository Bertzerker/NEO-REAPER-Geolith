"""MIT. Verify an exported bundle offline without extracting paths or executing content."""
import zipfile,json,hashlib,sys,struct
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
                if attachment:=note.get('attachment'):
                    assert all(attachment[key]==c[key] for key in ('frame','identity','generation','movie'))
                    assert attachment['capture']==c['id'],'Tracked capture mismatch'
                    packet=z.read(f"tracked/{attachment['id']}.bin")
                    assert hashlib.sha256(packet).hexdigest()==attachment['packetSha256'],'Tracked packet digest mismatch'
                    frame,x,y,w,h,mode=struct.unpack_from('<Q4HB',packet)
                    assert frame==c['frame'] and w>0 and h>0 and x+w<=304 and y+h<=224 and len(packet)==17+w*h*4
                    assert attachment['bounds']==dict(x=x,y=y,width=w,height=h),'Tracked bounds mismatch'
                    assert (mode,attachment['selection']) in ((0,'all'),(1,'instance')),'Tracked selection mismatch'
                    if version:=attachment['movieVersion']:
                        frozen_movie=z.read(f'movie-versions/{version}.json')
                        assert hashlib.sha256(frozen_movie).hexdigest()==version,'Movie revision mismatch'
                        row=attachment['movieInput']
                        saved=json.loads(frozen_movie)
                        assert saved['id']==c['movie'] and saved['identity']==c['identity']
                        assert row==c['movieInput']==saved['rows'][row['row']],'Tracked movie input mismatch'
                        assert hashlib.sha256(json.dumps(row,separators=(',',':')).encode()).hexdigest()==attachment['inputRowSha256']
        if 'movie.json' in names:
            movie=json.loads(z.read('movie.json'));assert movie['identity']==c['identity'] and movie['id']==c['movie'],'Movie identity mismatch'
            assert all(row['row']==i for i,row in enumerate(movie['rows'])),'Movie row order mismatch'
        print(json.dumps({'verified':True,'files':len(expected),'capture':c['id'],'frame':c['frame'],'identity':c['identity']}))
if __name__=='__main__': verify(sys.argv[1])
