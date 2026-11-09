"""Enable the approved one-time storefront without printing secrets."""
import os
import pathlib
import sys
p = pathlib.Path(sys.argv[1])
original = p.stat()
lines = p.read_text().splitlines()
values = {}
for line in lines:
    if '=' in line and not line.lstrip().startswith('#'):
        key, value = line.split('=', 1)
        values[key.strip()] = value.strip().strip('"').strip("'")
for key in ('GooglePlayBilling__Enabled', 'GooglePlayBilling__SalesEnabled'):
    if values.get(key, '').lower() != 'true':
        raise SystemExit('Stop: existing Play verification and sales must already be enabled.')
updates = {'GooglePlayBilling__OneTimeProductId': 'fitmate_ai_plus_month',
           'GooglePlayBilling__OneTimeMinimumAndroidVersionCode': '8',
           'GooglePlayBilling__OneTimeSalesEnabled': 'true'}
output = [line for line in lines if line.split('=', 1)[0].strip() not in updates]
output.extend(key + '=' + value for key, value in updates.items())
tmp = p.with_name(p.name + '.one-time-new')
with tmp.open('x') as handle:
    handle.write('\n'.join(output) + '\n')
os.chmod(tmp, 0o600)
os.chown(tmp, original.st_uid, original.st_gid)
os.replace(tmp, p)
print('ONE_TIME_CONFIG_PREPARED_NO_SECRETS_PRINTED')
