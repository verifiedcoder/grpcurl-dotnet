using System.Reflection;
using System.Runtime.InteropServices;
using GrpCurl.Net.Studio.Services.Secrets;

namespace GrpCurl.Net.Studio.Tests.Unit;

public sealed class LibsecretAbiTests
{
    [Fact]
    public void Schema_covers_the_complete_native_ABI_including_reference_count_and_reserved_tail()
    {
        // libsecret/secret-schema.h: name, flags, 32 attributes, reserved int, seven pointers.
        // Reflection keeps the interop type private; these constants describe the external ABI.
        var schema = typeof(LinuxLibsecretSecretStore).GetNestedType("SecretSchema", BindingFlags.NonPublic).ShouldNotBeNull();
        Marshal.SizeOf(schema).ShouldBe(IntPtr.Size == 8 ? 592 : 296);
        Marshal.OffsetOf(schema, "Reserved").ToInt32().ShouldBe(IntPtr.Size == 8 ? 528 : 264);
        Marshal.OffsetOf(schema, "Reserved7").ToInt32().ShouldBe(IntPtr.Size == 8 ? 584 : 292);
    }
}
