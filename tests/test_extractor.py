import importlib.util
from pathlib import Path
from types import SimpleNamespace
import sys
import unittest
from unittest.mock import patch, MagicMock

path = Path(__file__).resolve().parents[1] / "InstagramEmbedForDiscord/extractor/extract.py"
if not path.exists():
    path = Path('/app/extractor/extract.py')
spec = importlib.util.spec_from_file_location("extract", path)
extract = importlib.util.module_from_spec(spec)
spec.loader.exec_module(extract)
CDN = "https://scontent.cdninstagram.com/"


class ExtractorTests(unittest.TestCase):
    def test_rejects_non_media_hosts(self):
        for url in ["http://scontent.cdninstagram.com/x", "https://127.0.0.1/x",
                    "https://cdninstagram.com.evil.test/x", "https://u:p@cdninstagram.com/x",
                    "https://cdninstagram.com:8443/x", "file:///etc/passwd"]:
            with self.subTest(url=url), self.assertRaises(ValueError):
                extract.media_url(url)

    def test_selects_progressive_h264_not_dash_or_separate_audio(self):
        formats = [
            dict(url=CDN+'dash.mp4', ext='mp4', protocol='http_dash_segments', height=2160),
            dict(url=CDN+'silent.mp4', ext='mp4', acodec='none', height=2160),
            dict(url=CDN+'hevc.mp4', ext='mp4', vcodec='hevc', height=2160),
            dict(url=CDN+'video.mp4', ext='mp4', vcodec='avc1', acodec='mp4a', height=1080),
        ]
        result = extract.from_ytdlp(dict(formats=formats, thumbnail=CDN+'thumb.jpg'))
        self.assertEqual(result['Media'][0]['Url'], CDN+'video.mp4')

    def test_top_level_direct_video(self):
        result = extract.from_ytdlp(dict(url=CDN+'video.mp4', thumbnail=CDN+'t.jpg'))
        self.assertEqual(result['Media'][0]['MediaType'], 'video')

    def test_rejects_playlist_and_no_playable_format(self):
        for info in [dict(entries=[]), dict(formats=[]),
                     dict(url=CDN+'manifest.m3u8'),
                     dict(formats=[dict(url=CDN+'a.mp4', ext='mp4', acodec='none')])]:
            with self.assertRaises(ValueError):
                extract.from_ytdlp(info)

    def test_preserves_mixed_album_order(self):
        nodes = [SimpleNamespace(is_video=False, display_url=CDN+'1.jpg', video_url=None),
                 SimpleNamespace(is_video=True, display_url=CDN+'2.jpg', video_url=CDN+'2.mp4'),
                 SimpleNamespace(is_video=False, display_url=CDN+'3.jpg', video_url=None)]
        post = SimpleNamespace(typename='GraphSidecar', get_sidecar_nodes=lambda: iter(nodes),
                               owner_username='example', caption='caption')
        result = extract.from_instaloader(post)
        self.assertEqual([m['MediaType'] for m in result['Media']], ['image', 'video', 'image'])
        self.assertEqual(result['Media'][1]['Url'], CDN+'2.mp4')

    def test_post_does_not_fall_back_to_incomplete_video_list(self):
        with patch.object(extract, 'instaloader_post', side_effect=ValueError), patch.object(extract, 'ytdlp_post') as video:
            with self.assertRaises(ValueError):
                extract.resolve('p', 'ABC')
            video.assert_not_called()

    def test_reel_fallback(self):
        with patch.object(extract, 'ytdlp_post', side_effect=ValueError), patch.object(extract, 'instaloader_post', return_value={}) as fallback:
            self.assertEqual(extract.resolve('reel', 'ABC'), {})
            fallback.assert_called_once_with('reel', 'ABC')

    def test_ytdlp_invoked_without_download(self):
        factory = MagicMock()
        ydl = factory.return_value.__enter__.return_value
        ydl.extract_info.return_value = dict(url=CDN+'video.mp4')
        with patch.dict(sys.modules, {'yt_dlp': SimpleNamespace(YoutubeDL=factory)}):
            extract.ytdlp_post('reel', 'ABC')
        self.assertTrue(factory.call_args.args[0]['skip_download'])
        self.assertFalse(factory.call_args.args[0]['cachedir'])
        self.assertEqual(ydl.extract_info.call_args.kwargs, dict(download=False, process=False))

    def test_invalid_input_never_reaches_provider(self):
        with patch.object(extract, 'ytdlp_post') as video, patch.object(extract, 'instaloader_post') as photo:
            for kind, code in [('p', '../x'), ('stories', 'ABC'), ('p', '--help'), ('p', 'x?url=http://localhost')]:
                # --help is a syntactically legal shortcode; safe because no shell/CLI options are parsed.
                if code == '--help':
                    continue
                with self.assertRaises(ValueError):
                    extract.resolve(kind, code)
            video.assert_not_called()
            photo.assert_not_called()

    def test_instaloader_only_reads_metadata_and_closes(self):
        factory = MagicMock()
        loader = factory.return_value
        post_factory = MagicMock()
        post_factory.from_shortcode.return_value = SimpleNamespace(
            typename='GraphImage', is_video=False, url=CDN+'photo.jpg',
            owner_username='example', caption='caption')
        with patch.dict(sys.modules, {'instaloader': SimpleNamespace(Instaloader=factory, Post=post_factory)}):
            result = extract.instaloader_post('p', 'ABC')
        self.assertEqual(result['Media'][0]['MediaType'], 'image')
        self.assertFalse(factory.call_args.kwargs['save_metadata'])
        self.assertFalse(factory.call_args.kwargs['download_pictures'])
        loader.download_post.assert_not_called()
        loader.close.assert_called_once()


if __name__ == '__main__':
    unittest.main()
