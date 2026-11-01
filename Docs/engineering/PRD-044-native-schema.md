# PRD-044: Native Studio test-host crash

## Root cause and correction

The Studio test host's exit 139 was a real memory-safety failure, not a passing
run with noisy teardown. Microsoft symbols resolve the recorded .NET 10.0.0
`libcoreclr.so` offsets to GC marking/object access. A standalone program using
only the original libsecret interop declaration, concurrent lookups and forced GC
reproduced the same `0x5d77c2` fault. The Studio diagnostic batch also crashed on
run 9, with a dump and kernel record at the previously observed `0x5d6e6d` offset.

The managed `SecretSchema` omitted the native reserved tail. On x64 it occupied
528 bytes instead of 592. The reference count at native offset 528 is used by
libsecret, even by synchronous operations. Consequently native reference handling
could read/write beyond the marshaller's stack buffer and temporarily corrupt
adjacent managed roots while a GC ran. See the upstream
[schema reference implementation](https://gnome.pages.gitlab.gnome.org/libsecret/coverage/libsecret/secret-schema.c.gcov.html).

The corrected declaration includes the reserved integer and all seven pointers,
zero-initialized so the library treats it as a static schema. This preserves the
existing schema name, attributes, secret keys, storage and consent behavior.
The otherwise identical corrected standalone probe completed 100,000 concurrent
lookups under forced compacting GC. `LibsecretAbiTests` locks the external ABI
size and tail offsets on both pointer widths; the native headless integration
suite continues to exercise the library itself.

## Verification discipline

Build Release, then run `Scripts/check-studio-stability.sh 5 /tmp/studio-stability`.
It runs the complete Studio unit module each time and stops at the first nonzero
exit or native-crash marker. It never retries a failure into a green result.
Use the full solution command and three-OS CI for regression validation as well.
A finite clean batch provides evidence, not a guarantee against every future failure.

The companion PRD-044 fix-review record contains reproduction source, exact logs,
crash symbolization, stress results and review status. Dumps remain local and are
not published. The two slow dead-address tests were excluded only from the
initial diagnostic repetition; the complete verification runs include them.
This correction was required during PRD-025 validation at the user's request;
it is recorded in a separate commit and is not part of the update-link policy.
