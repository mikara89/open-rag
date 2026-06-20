using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenRAG.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_processing_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunReason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_processing_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "document_processing_steps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessingRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    InputHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OutputHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ProcessorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProcessorVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_processing_steps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CurrentVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "document_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    OriginalObjectKey = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    OriginalContentType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OriginalSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    OriginalSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DoclingMarkdownObjectKey = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DoclingJsonObjectKey = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_document_versions_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_processing_runs_TenantId_CorrelationId",
                table: "document_processing_runs",
                columns: new[] { "TenantId", "CorrelationId" });

            migrationBuilder.CreateIndex(
                name: "IX_document_processing_runs_TenantId_DocumentId",
                table: "document_processing_runs",
                columns: new[] { "TenantId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_document_processing_runs_TenantId_Status",
                table: "document_processing_runs",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_document_processing_runs_TenantId_VersionId",
                table: "document_processing_runs",
                columns: new[] { "TenantId", "VersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_document_processing_steps_TenantId_DocumentId",
                table: "document_processing_steps",
                columns: new[] { "TenantId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_document_processing_steps_TenantId_ProcessingRunId",
                table: "document_processing_steps",
                columns: new[] { "TenantId", "ProcessingRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_document_processing_steps_TenantId_ProcessingRunId_StepName",
                table: "document_processing_steps",
                columns: new[] { "TenantId", "ProcessingRunId", "StepName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_processing_steps_TenantId_Status",
                table: "document_processing_steps",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_document_processing_steps_TenantId_StepName",
                table: "document_processing_steps",
                columns: new[] { "TenantId", "StepName" });

            migrationBuilder.CreateIndex(
                name: "IX_document_processing_steps_TenantId_VersionId",
                table: "document_processing_steps",
                columns: new[] { "TenantId", "VersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_document_versions_DocumentId",
                table: "document_versions",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_document_versions_TenantId_DocumentId",
                table: "document_versions",
                columns: new[] { "TenantId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_document_versions_TenantId_DocumentId_Id",
                table: "document_versions",
                columns: new[] { "TenantId", "DocumentId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_document_versions_TenantId_DocumentId_VersionNumber",
                table: "document_versions",
                columns: new[] { "TenantId", "DocumentId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_versions_TenantId_Status",
                table: "document_versions",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_documents_TenantId",
                table: "documents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_documents_TenantId_CurrentVersionId",
                table: "documents",
                columns: new[] { "TenantId", "CurrentVersionId" });

            migrationBuilder.CreateIndex(
                name: "IX_documents_TenantId_Id",
                table: "documents",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_documents_TenantId_Status",
                table: "documents",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_processing_runs");

            migrationBuilder.DropTable(
                name: "document_processing_steps");

            migrationBuilder.DropTable(
                name: "document_versions");

            migrationBuilder.DropTable(
                name: "documents");
        }
    }
}
