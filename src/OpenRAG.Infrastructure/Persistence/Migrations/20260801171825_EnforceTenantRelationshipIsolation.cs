using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenRAG.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceTenantRelationshipIsolation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_document_versions_documents_DocumentId",
                table: "document_versions");

            migrationBuilder.DropIndex(
                name: "IX_documents_TenantId_Id",
                table: "documents");

            migrationBuilder.DropIndex(
                name: "IX_document_versions_DocumentId",
                table: "document_versions");

            migrationBuilder.DropIndex(
                name: "IX_document_versions_TenantId_DocumentId_Id",
                table: "document_versions");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_documents_TenantId_Id",
                table: "documents",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_document_versions_TenantId_DocumentId_Id",
                table: "document_versions",
                columns: new[] { "TenantId", "DocumentId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_document_processing_runs_TenantId_DocumentId_VersionId_Id",
                table: "document_processing_runs",
                columns: new[] { "TenantId", "DocumentId", "VersionId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_document_chunks_TenantId_DocumentId_VersionId_Id",
                table: "document_chunks",
                columns: new[] { "TenantId", "DocumentId", "VersionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_document_processing_steps_TenantId_DocumentId_VersionId_Pro~",
                table: "document_processing_steps",
                columns: new[] { "TenantId", "DocumentId", "VersionId", "ProcessingRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_document_intelligence_TenantId_DocumentId_VersionId",
                table: "document_intelligence",
                columns: new[] { "TenantId", "DocumentId", "VersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_document_embeddings_TenantId_DocumentId_VersionId_ChunkId",
                table: "document_embeddings",
                columns: new[] { "TenantId", "DocumentId", "VersionId", "ChunkId" });

            migrationBuilder.AddForeignKey(
                name: "FK_document_chunks_document_versions_TenantId_DocumentId_Versi~",
                table: "document_chunks",
                columns: new[] { "TenantId", "DocumentId", "VersionId" },
                principalTable: "document_versions",
                principalColumns: new[] { "TenantId", "DocumentId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_document_embeddings_document_chunks_TenantId_DocumentId_Ver~",
                table: "document_embeddings",
                columns: new[] { "TenantId", "DocumentId", "VersionId", "ChunkId" },
                principalTable: "document_chunks",
                principalColumns: new[] { "TenantId", "DocumentId", "VersionId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_document_intelligence_document_versions_TenantId_DocumentId~",
                table: "document_intelligence",
                columns: new[] { "TenantId", "DocumentId", "VersionId" },
                principalTable: "document_versions",
                principalColumns: new[] { "TenantId", "DocumentId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_document_processing_runs_document_versions_TenantId_Documen~",
                table: "document_processing_runs",
                columns: new[] { "TenantId", "DocumentId", "VersionId" },
                principalTable: "document_versions",
                principalColumns: new[] { "TenantId", "DocumentId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_document_processing_steps_document_processing_runs_TenantId~",
                table: "document_processing_steps",
                columns: new[] { "TenantId", "DocumentId", "VersionId", "ProcessingRunId" },
                principalTable: "document_processing_runs",
                principalColumns: new[] { "TenantId", "DocumentId", "VersionId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_document_versions_documents_TenantId_DocumentId",
                table: "document_versions",
                columns: new[] { "TenantId", "DocumentId" },
                principalTable: "documents",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_document_chunks_document_versions_TenantId_DocumentId_Versi~",
                table: "document_chunks");

            migrationBuilder.DropForeignKey(
                name: "FK_document_embeddings_document_chunks_TenantId_DocumentId_Ver~",
                table: "document_embeddings");

            migrationBuilder.DropForeignKey(
                name: "FK_document_intelligence_document_versions_TenantId_DocumentId~",
                table: "document_intelligence");

            migrationBuilder.DropForeignKey(
                name: "FK_document_processing_runs_document_versions_TenantId_Documen~",
                table: "document_processing_runs");

            migrationBuilder.DropForeignKey(
                name: "FK_document_processing_steps_document_processing_runs_TenantId~",
                table: "document_processing_steps");

            migrationBuilder.DropForeignKey(
                name: "FK_document_versions_documents_TenantId_DocumentId",
                table: "document_versions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_documents_TenantId_Id",
                table: "documents");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_document_versions_TenantId_DocumentId_Id",
                table: "document_versions");

            migrationBuilder.DropIndex(
                name: "IX_document_processing_steps_TenantId_DocumentId_VersionId_Pro~",
                table: "document_processing_steps");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_document_processing_runs_TenantId_DocumentId_VersionId_Id",
                table: "document_processing_runs");

            migrationBuilder.DropIndex(
                name: "IX_document_intelligence_TenantId_DocumentId_VersionId",
                table: "document_intelligence");

            migrationBuilder.DropIndex(
                name: "IX_document_embeddings_TenantId_DocumentId_VersionId_ChunkId",
                table: "document_embeddings");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_document_chunks_TenantId_DocumentId_VersionId_Id",
                table: "document_chunks");

            migrationBuilder.CreateIndex(
                name: "IX_documents_TenantId_Id",
                table: "documents",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_document_versions_DocumentId",
                table: "document_versions",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_document_versions_TenantId_DocumentId_Id",
                table: "document_versions",
                columns: new[] { "TenantId", "DocumentId", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_document_versions_documents_DocumentId",
                table: "document_versions",
                column: "DocumentId",
                principalTable: "documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
