using Acme.Core.Files;
using Acme.Core.Platform;
using Acme.PurchaseRequest.Application;

namespace Acme.PurchaseRequest.Infrastructure;

/// <summary>Supplier quotes: PDFs, images, office documents and CSV. The real content type is checked from the bytes.</summary>
internal sealed class QuoteFiles(PurchaseRequestDbContext db, FileService files) : IQuoteFiles
{
    /// <summary>The file grouping in the object key: the quote of one request.</summary>
    private const string Entity = "quote";

    private static readonly string[] AllowedTypes =
    [
        "application/pdf", "image/png", "image/jpeg",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "text/csv",
    ];

    public async Task<Guid> UploadAsync(Guid requestId, Stream content, string claimedName, CancellationToken cancellationToken = default)
    {
        var file = await files.UploadAsync(db, new FileUpload(Modules.PurchaseRequest, Entity, requestId, content, claimedName, AllowedTypes), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);   // the file row; the request links to it in its own save
        return file.Id;
    }

    public Task<Uri> DownloadUrlAsync(Guid fileId, CancellationToken cancellationToken = default) =>
        files.GetDownloadUrlAsync(db, fileId, cancellationToken);
}
