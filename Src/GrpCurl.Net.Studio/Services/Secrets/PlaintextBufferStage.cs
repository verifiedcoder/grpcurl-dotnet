namespace GrpCurl.Net.Studio.Services.Secrets;

// Instance-scoped test checkpoints run inside the owning buffer's try/finally. Tests can retain
// the actual array or throw here; production supplies no callback and never logs these buffers.
internal enum PlaintextBufferStage
{
    EncryptInput,
    DecryptAllocated,
    DecryptOutput
}
