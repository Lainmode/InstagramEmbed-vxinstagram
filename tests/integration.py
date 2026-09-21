"""Exercise the real HTTP app and subprocess/cache boundary without Instagram access.

Usage: python tests/integration.py PATH/TO/InstagramEmbed.Application.dll
"""
import concurrent.futures
import json
import os
from pathlib import Path
import shutil
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


def main():
    dll = Path(sys.argv[1]).resolve()
    with tempfile.TemporaryDirectory() as temp:
        root = Path(temp)
        (root/'extractor').mkdir()
        (root/'wwwroot').mkdir()
        shutil.copyfile(Path(__file__).with_name('fixture_extractor.py'), root/'extractor/extract.py')
        calls = root/'calls.txt'
        with socket.socket() as sock:
            sock.bind(('127.0.0.1', 0))
            port = sock.getsockname()[1]
        base = f'http://127.0.0.1:{port}'
        env = dict(os.environ, ASPNETCORE_URLS=base, Instance__PublicUrl='https://ig.example.test',
                   Extractor__Python=sys.executable, Extractor__TimeoutSeconds='5',
                   Extractor__CacheSeconds='10', TEST_CALLS=str(calls),
                   Logging__LogLevel__Default='Warning')
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())

        def request(path, method='GET', headers=None):
            try:
                result = opener.open(urllib.request.Request(base+path, method=method, headers=headers or {}), timeout=10)
            except urllib.error.HTTPError as error:
                result = error
            with result:
                return result.status, result.headers, result.read().decode()

        with (root/'app.log').open('w+') as log:
            process = subprocess.Popen(['dotnet', str(dll), '--contentRoot', str(root)], cwd=root, env=env,
                                       stdout=log, stderr=subprocess.STDOUT)
            try:
                for _ in range(100):
                    try:
                        if request('/healthz')[0] == 200:
                            break
                    except OSError:
                        pass
                    if process.poll() is not None:
                        raise AssertionError('App failed to start')
                    time.sleep(.1)
                else:
                    raise AssertionError('Startup timeout')
                assert request('/')[0] == 200
                status, _, html = request('/reel/VIDEO/', headers={'Host': 'attacker.example'})
                assert status == 200 and 'https://ig.example.test/media/reel/VIDEO/0' in html
                assert 'attacker.example/media' not in html
                assert '<script>alert(1)</script>' not in html and '&lt;script&gt;' in html
                status, headers, _ = request('/media/reel/VIDEO/0')
                assert status == 302 and headers['Location'].startswith('https://scontent.cdninstagram.com/one.mp4')
                assert headers['Cache-Control'] == 'no-store'
                assert request('/media/reel/VIDEO/0', method='HEAD')[0] == 302
                assert request('/media/reel/VIDEO/0?thumbnail=true')[1]['Location'].endswith('one.jpg')
                status, _, html = request('/p/ALBUM/2/')
                assert status == 200 and 'og:video' in html and '/media/p/ALBUM/1' in html
                assert 'application/activity+json' not in html
                assert '/media/p/ALBUM/1' in request('/p/ALBUM/?img_index=2')[2]
                assert request('/p/ALBUM/99/')[0] == 404
                assert request('/p/ALBUM/?img_index=0')[0] == 400
                assert request('/p/ALBUM/?img_index=bad')[0] == 400
                assert 'application/activity+json' in request('/p/ALBUM/')[2]
                activity = json.loads(request('/api/v1/statuses/p/ALBUM')[2])
                assert request('/users/instagram/statuses/p.ALBUM')[0] == 200
                assert request('/api/v1/statuses/p.ALBUM')[0] == 200
                assert request('/api/v1/statuses/invalid-token')[0] == 404
                assert [m['type'] for m in activity['media_attachments']] == ['image', 'video', 'image']
                assert '<script>' not in activity['content']
                assert request('/setdonationvariables?pw=password&current=10')[0] == 404
                oembed = json.loads(request('/oembed?donateMessage=INJECTED')[2])
                assert oembed['provider_url'] == 'https://ig.example.test'
                assert 'INJECTED' not in json.dumps(oembed)
                for path in ['/stories/user/123/', '/share/reel/ABC/', '/media/evil/ABC/0', '/media/p/ABC/99']:
                    assert request(path)[0] == 404, path
                with concurrent.futures.ThreadPoolExecutor() as pool:
                    assert list(pool.map(lambda _: request('/reel/DUP/')[0], range(4))) == [200]*4
                assert calls.read_text().splitlines().count('reel:DUP') == 1
                for code in ['FAIL', 'BIG', 'BADHOST']:
                    assert request(f'/reel/{code}/')[0] == 503
                assert request('/reel/FAIL/')[0] == 503
                assert calls.read_text().splitlines().count('reel:FAIL') == 1
                start = time.monotonic()
                assert request('/reel/SLOW/')[0] == 503
                assert time.monotonic()-start < 8
                # After expiry, the media URL still carries the original kind and ID.
                time.sleep(10.2)
                assert request('/media/reel/VIDEO/0')[0] == 302
                assert calls.read_text().splitlines().count('reel:VIDEO') == 2
                log.flush()
                assert 'secret-signed-url-do-not-log' not in (root/'app.log').read_text()
                print('HTTP integration checks passed: embeds, album order, redirects/HEAD, escaping, validation, cache, duplicate requests, failures, timeout, expiry.')
            except Exception:
                log.flush()
                print((root/'app.log').read_text(), file=sys.stderr)
                raise
            finally:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()


if __name__ == '__main__':
    main()
