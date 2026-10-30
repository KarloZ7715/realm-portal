# Minisign test fixtures (test only)

Everything here is for `MinisignVerifierTests` and nothing else.

- `test-key-a.pub`, `test-key-b.pub` and `test-key-unknown.pub` are public keys of throwaway key pairs made by
  `make-fixtures.sh`. The secret keys were deleted right after signing. None of them is, or will ever be, a release key, and
  the app does not trust them.
- `SHA256SUMS` and `SHA256SUMS-1.1.9` are synthetic. Their digests are of placeholder strings, not of real packages.
- The `.minisig` files were made with the real `minisign` CLI (0.12) and checked with `minisign -V -H` (the legacy one with
  `minisign -V`).

To regenerate, install minisign and run `./make-fixtures.sh`. Every run makes new keys, so all files change together.

The `.gitattributes` here keeps the files byte-exact; a CRLF conversion would break the signatures.
