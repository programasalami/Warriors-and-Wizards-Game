import re,collections,sys
t=open(sys.argv[1],encoding='utf-8',errors='ignore').read()
errs=set()
for line in t.splitlines():
    if ' error ' in line:
        line=re.sub(r'\s*\[C:.*\]$','',line)
        errs.add(line.strip())
print(len(errs),'unique errors')
c=collections.Counter(); ex={}
for e in errs:
    m=re.search(r'error (CS\d+): (.*)',e)
    k=(m.group(1),m.group(2)[:120]) if m else (e[:100],'')
    c[k]+=1; ex.setdefault(k,e)
lim=int(sys.argv[2]) if len(sys.argv)>2 else 40
for k,n in c.most_common(lim): print(n,k[0],k[1]); 
if len(sys.argv)>3:
    for k,n in c.most_common(lim): print(ex[k][:260])
