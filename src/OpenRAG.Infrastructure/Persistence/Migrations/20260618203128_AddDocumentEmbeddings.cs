using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenRAG.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Enable pgvector extension for future vector column migration
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

            migrationBuilder.CreateTable(
                name: "document_embeddings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChunkId = table.Column<Guid>(type: "uuid", nullable: false),
                    Vector = table.Column<byte[]>(type: "bytea", nullable: false),
                    EmbeddingProvider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EmbeddingModel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EmbeddingDimensions = table.Column<int>(type: "integer", nullable: false),
                    EmbeddingVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_embeddings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_embeddings_TenantId_ChunkId",
                table: "document_embeddings",
                columns: new[] { "TenantId", "ChunkId" });

            migrationBuilder.CreateIndex(
                name: "IX_document_embeddings_TenantId_ChunkId_EmbeddingModel",
                table: "document_embeddings",
                columns: new[] { "TenantId", "ChunkId", "EmbeddingModel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_embeddings_TenantId_DocumentId",
                table: "document_embeddings",
                columns: new[] { "TenantId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_document_embeddings_TenantId_VersionId",
                table: "document_embeddings",
                columns: new[] { "TenantId", "VersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_document_embeddings_TenantId_VersionId_EmbeddingModel",
                table: "document_embeddings",
                columns: new[] { "TenantId", "VersionId", "EmbeddingModel" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_embeddings");
        }
    }
}
