"""MIT. Sustained native observer workload; original diagnostic only."""
from native_test import *
import statistics
seconds=float(sys.argv[1]) if len(sys.argv)>1 else 180
l=load(0)
for _ in range(10):l.nr_step(0,0)
initial=state(l);results={}
for observed,duration in [(0,10),(1,seconds)]:
    assert l.nr_restore(initial,len(initial),10);l.nr_observer(observed)
    costs=[];start=time.perf_counter();frames=0
    while time.perf_counter()-start<duration:
        t=time.perf_counter();l.nr_step((frames//60%2)*16,0);costs.append((time.perf_counter()-t)*1000);frames+=1
    costs.sort();results[str(observed)]={'seconds':time.perf_counter()-start,'frames':frames,'nativeFpsUnthrottled':frames/(time.perf_counter()-start),'stepP95Ms':costs[int((len(costs)-1)*.95)],'stepMeanMs':statistics.mean(costs)}
    print(json.dumps({str(observed):results[str(observed)]}),flush=True)
results['meanObserverOverheadPercent']=(results['1']['stepMeanMs']/results['0']['stepMeanMs']-1)*100
results['scope']='Actual AES Geolith original test-only firmware/cartridge; CPU execution only, no host copies/encoding/viewer; no audio playback; unthrottled throughput'
results['coreDllSha256']=sha((ROOT/'bin'/'neo-reaper-core.dll').read_bytes())
(ROOT/'test-output'/'native-performance.json').write_text(json.dumps(results,indent=2));print(json.dumps(results),flush=True)
