"""Validate running local resources; output one JSON record per resource."""
import argparse
import json
from .config import load_settings
from .resources import ResourceSamples, RESOURCE_NAMES


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("resources", nargs="*", choices=RESOURCE_NAMES)
    parser.add_argument("--settings")
    parser.add_argument("--text", default="Hello from Python")
    args = parser.parse_args()
    samples = ResourceSamples(load_settings(args.settings))
    failed = False
    try:
        for resource in args.resources or RESOURCE_NAMES:
            try:
                print(json.dumps({"resource": resource, "success": True, "result": samples.run(resource, args.text)}))
            except Exception:
                print(json.dumps({"resource": resource, "success": False, "error": "Local resource validation failed; check its configuration and running container."}))
                failed = True
    finally:
        samples.close()
    return int(failed)


if __name__ == "__main__":
    raise SystemExit(main())
