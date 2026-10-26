import json
import threading
import unittest
from urllib.error import HTTPError
from urllib.request import Request, urlopen
from http.server import HTTPServer

from bridge.termux_controller import Handler


class ControllerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = HTTPServer(("127.0.0.1", 0), Handler)
        cls.thread = threading.Thread(target=cls.server.serve_forever, daemon=True)
        cls.thread.start()
        cls.base = f"http://127.0.0.1:{cls.server.server_port}"

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()
        cls.server.server_close()
        cls.thread.join(timeout=2)

    def test_health_is_demo_only_and_no_orders(self):
        with urlopen(self.base + "/health", timeout=2) as response:
            data = json.loads(response.read())
        self.assertEqual(data["status"], "ok")
        self.assertFalse(data["broker_connected"])
        self.assertFalse(data["orders_enabled"])
        self.assertEqual(data["mode"], "demo-only")

    def test_post_control_is_forbidden(self):
        request = Request(self.base + "/anything", data=b"{}", method="POST")
        with self.assertRaises(HTTPError) as caught:
            urlopen(request, timeout=2)
        self.assertEqual(caught.exception.code, 403)


if __name__ == "__main__":
    unittest.main()
