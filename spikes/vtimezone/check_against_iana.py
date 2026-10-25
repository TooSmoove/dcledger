import sys
from datetime import datetime, timedelta, timezone
from zoneinfo import ZoneInfo
import icalendar
raw = open(sys.argv[1], 'rb').read()
cal = icalendar.Calendar.from_ical(b"BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//dc-ledger//spike//EN\r\n" + raw + b"END:VCALENDAR\r\n")
vtz = cal.walk('VTIMEZONE')[0]
tz = vtz.to_tz(lookup_tzid=False)  # build tzinfo from OUR block, not the system db
ref = ZoneInfo('America/New_York')
# golden instants
for local, want in [((2026,10,15,17,30), '2026-10-15T21:30:00+00:00'), ((2026,11,5,17,30), '2026-11-05T22:30:00+00:00')]:
    got = datetime(*local, tzinfo=tz).astimezone(timezone.utc).isoformat()
    print('golden', local, got, 'OK' if got == want else f'FAIL want {want}')
# exhaustive: every 15 minutes of 2007..2037 compared with IANA via UTC instants
mism = 0; t = datetime(2007,1,1,tzinfo=timezone.utc); end = datetime(2038,1,1,tzinfo=timezone.utc)
step = timedelta(minutes=15); n = 0
while t < end:
    a = t.astimezone(tz).utcoffset(); b = t.astimezone(ref).utcoffset()
    if a != b:
        mism += 1
        if mism <= 5: print('mismatch', t, a, b)
    t += step; n += 1
print('instants checked', n, 'mismatches', mism)
# DST-gap and overlap wall times around Nov 1 2026 and Mar 14 2027
for wall in [(2026,11,1,1,30), (2027,3,14,2,30)]:
    for fold in (0,1):
        a = datetime(*wall, fold=fold, tzinfo=tz).utcoffset(); b = datetime(*wall, fold=fold, tzinfo=ref).utcoffset()
        print('wall', wall, 'fold', fold, a, b, 'OK' if a == b else 'DIFF')
