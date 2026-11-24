"""Start free Moto and restore the default topology on every process start."""

import signal
import threading

from init import initialize
from local_session import create_session


def run():
    """Let Compose own lifecycle; never launch runtime containers through a socket."""
    from moto.server import ThreadedMotoServer

    stopped = threading.Event()
    for name in (signal.SIGTERM, signal.SIGINT):
        signal.signal(name, lambda *_: stopped.set())
    server = ThreadedMotoServer(ip_address="0.0.0.0", port=5000, verbose=False)
    server.start()
    try:
        initialize(create_session("http://localhost:5000"))
        stopped.wait()
    finally:
        server.stop()


if __name__ == "__main__":
    try:
        run()
    except Exception:
        raise SystemExit("Moto startup failed; default AWS resources could not be initialized.") from None
