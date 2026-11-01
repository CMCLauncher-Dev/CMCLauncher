"""Keep the latest stable and beta ZIP in the release mirror."""
import os
import re
from pathlib import Path

import boto3


def channel(name):
    match = re.fullmatch(r"CMCLauncher-\d+\.\d+\.\d+(-beta(?:[.-]\d+)?)?-win-x64\.zip", name)
    return ("beta" if match[1] else "release") if match else None


def main():
    archive = Path(os.environ["RELEASE_ARCHIVE"])
    selected = channel(archive.name)
    if selected is None:
        print("Skipping mirror sync for this release channel.")
        return
    if not archive.is_file() or archive.stat().st_size == 0:
        raise RuntimeError("Release archive is missing or empty")
    for key in ("AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY"):
        if not os.environ.get(key):
            raise RuntimeError(f"Missing Actions secret: {key}")
    client = boto3.client("s3", endpoint_url="https://cn-nb1.rains3.com", region_name="cn-nb1")
    bucket, prefix = "ctiso", "CMCL Release/"
    # Enumerate before deleting. Never touch other folders, channels, or unrelated objects.
    old = []
    for page in client.get_paginator("list_objects_v2").paginate(Bucket=bucket, Prefix=prefix):
        for obj in page.get("Contents", []):
            name = obj["Key"][len(prefix):]
            if channel(name) == selected:
                old.append(obj["Key"])
    for key in old:
        client.delete_object(Bucket=bucket, Key=key)
    target = prefix + archive.name
    client.upload_file(str(archive), bucket, target, ExtraArgs={"ContentType": "application/zip"})
    info = client.head_object(Bucket=bucket, Key=target)
    if info["ContentLength"] != archive.stat().st_size:
        raise RuntimeError("Uploaded archive size mismatch")
    print(f"Release mirror updated: {selected}")


if __name__ == "__main__":
    main()
