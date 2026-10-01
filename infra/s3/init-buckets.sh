#!/bin/sh
# Creates the blob-storage buckets and the frames-temp expiry rule. Runs once per
# `docker compose up` in the s3-init container; safe to re-run (existing buckets are kept).
# Uses only standard S3 API calls, so the same steps work against AWS S3.
set -eu

S3="${S3_ENDPOINT:-http://s3:8333}"
MASTER="${SEAWEED_MASTER:-s3:9333}"

for bucket in frames-temp frames-permanent evidence; do
  code=$(curl -s -o /dev/null -w '%{http_code}' -X PUT "$S3/$bucket")
  case "$code" in
    200) echo "created bucket $bucket" ;;
    409) echo "bucket $bucket already exists" ;;
    *)   echo "creating bucket $bucket failed: HTTP $code" >&2; exit 1 ;;
  esac
done

# SKELETON: temp frames expire after 30 days. The real retention window is still open
# (design record §15); change <Days> here once it is decided.
curl -sf -X PUT -H 'Content-Type: application/xml' "$S3/frames-temp?lifecycle" --data-binary \
  '<LifecycleConfiguration><Rule><ID>expire-after-30-days</ID><Filter><Prefix></Prefix></Filter><Status>Enabled</Status><Expiration><Days>30</Days></Expiration></Rule></LifecycleConfiguration>'
echo "frames-temp lifecycle: expire after 30 days"

# SeaweedFS-only (dev): apply the rule as a native TTL on every new object instead of
# waiting for the daily lifecycle pass. Not needed on AWS S3.
echo "s3.bucket.lifecycle.fastpath -name frames-temp -enable" | weed shell -master="$MASTER"
