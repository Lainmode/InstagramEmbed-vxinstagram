"""Offline subprocess fixture; only copied into a temporary test content root."""
import json
import os
import sys
import time
from pathlib import Path

kind, code, provider = sys.argv[1:]
with Path(os.environ['TEST_CALLS']).open('a') as f:
    f.write(f'{kind}:{code}\n')
if code == 'FAIL':
    print('secret-signed-url-do-not-log', file=sys.stderr)
    sys.exit(1)
if code == 'SLOW':
    time.sleep(20)
if code == 'BIG':
    print('x' * 1100000)
    sys.exit(0)
if code == 'DUP':
    time.sleep(.3)
cdn = 'https://scontent.cdninstagram.com/'
media = [dict(Url=cdn+'one.mp4?token=example', MediaType='video', ThumbnailUrl=cdn+'one.jpg', Width=1080, Height=1920)]
if code == 'ALBUM':
    media = [dict(Url=cdn+'first.jpg', MediaType='image', ThumbnailUrl=cdn+'first.jpg'),
             media[0], dict(Url=cdn+'last.jpg', MediaType='image', ThumbnailUrl=cdn+'last.jpg')]
if code == 'BADHOST':
    media[0]['Url'] = 'https://127.0.0.1/private'
print(json.dumps(dict(AuthorUsername='example', Caption='<script>alert(1)</script>', Media=media)))
