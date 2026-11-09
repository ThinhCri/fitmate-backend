#!/usr/bin/env python3
"""Add a narrowly scoped SignalR proxy to the known admin Nginx vhost."""
from pathlib import Path
import sys
p = Path(sys.argv[1]).resolve()
if str(p) not in ('/etc/nginx/sites-available/fitmate-admin', '/etc/nginx/sites-enabled/fitmate-admin'):
    raise SystemExit('Unexpected admin proxy path')
s = p.read_text()
if 'location /api/v1/admin/finance/live {' in s:
    raise SystemExit(0)
anchor = '    location /api/ {'
if s.count(anchor) != 1:
    raise SystemExit('Admin API proxy must have exactly one insertion anchor')
block = '''    location /api/v1/admin/finance/live {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 120s;
        proxy_buffering off;
        access_log off;
    }

'''
tmp = p.with_name(p.name + '.finance-new')
tmp.write_text(s.replace(anchor, block + anchor))
tmp.chmod(0o644)
tmp.replace(p)
