using Gql2Grpc.Configuration;
using Gql2Grpc.GraphQL;

namespace Gql2Grpc.Translation;

/// <summary>
///     Turns a resolved GraphQL root selection and its mapping entry into the JSON body accepted
///     by GrpCurl.Net's <c>DynamicInvoker</c> for the target request message.
/// </summary>
public interface IRequestTranslator
{
    /// <summary>
    ///     Builds the JSON request body that will be marshalled to the gRPC method's input message.
    /// </summary>
    /// <param name="root">The resolved root selection (already with fragments inlined and arguments coerced).</param>
    /// <param name="entry">The mapping entry that resolved this selection to a service/method.</param>
    /// <param name="defaults">Mapping-wide defaults applied as a fallback (e.g., argument aliases).</param>
    /// <returns>JSON string suitable for <c>SimpleDynamicMessage</c> construction.</returns>
    string Translate(ResolvedSelection root, MappingEntry entry, MappingDefaults defaults);
}