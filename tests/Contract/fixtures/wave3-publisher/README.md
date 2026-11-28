# Publisher signature test vector

The committed manifest, Ed25519 public key and detached signature form a fixed known-answer vector. The manifest includes its final LF. A local process generated a one-time random Ed25519 key in memory, signed the exact manifest bytes and exited after writing the public files.

From this directory, verify the digest and signature with Python 3 and OpenSSL 3.

```sh
echo '82478c0cc6aa2e468f3fe87f73a13f4b2e482bdeb764e5947bf8b9e5083d1c52  manifest.json' | sha256sum -c -
sigfile=$(mktemp)
trap 'rm -f "$sigfile"' EXIT
python3 -c 'import base64,json,sys; sys.stdout.buffer.write(base64.urlsafe_b64decode(json.load(open("manifest.json.sig.json"))["signature"] + "=="))' > "$sigfile"
openssl pkeyutl -verify -pubin -inkey public.pem -rawin -in manifest.json -sigfile "$sigfile"
```
