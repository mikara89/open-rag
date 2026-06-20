using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenRAG.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentChunks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_chunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: true),
                    SectionTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TokenCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_chunks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_chunks_TenantId_ContentHash",
                table: "document_chunks",
                columns: new[] { "TenantId", "ContentHash" });

            migrationBuilder.CreateIndex(
                name: "IX_document_chunks_TenantId_DocumentId",
                table: "document_chunks",
                columns: new[] { "TenantId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_document_chunks_TenantId_DocumentId_VersionId",
                table: "document_chunks",
                columns: new[] { "TenantId", "DocumentId", "VersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_document_chunks_TenantId_DocumentId_VersionId_ChunkIndex",
                table: "document_chunks",
                columns: new[] { "TenantId", "DocumentId", "VersionId", "ChunkIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_chunks_TenantId_VersionId",
                table: "document_chunks",
                columns: new[] { "TenantId", "VersionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_chunks");
        }
    }
}
