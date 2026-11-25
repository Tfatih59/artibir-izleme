using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Artibir.Izleme.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_users",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    external_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    last_seen_utc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "faults",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    plant_id = table.Column<int>(type: "integer", nullable: false),
                    turbine_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    occurred_at_local = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    responsible = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_faults", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hourly_productions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    plant_id = table.Column<int>(type: "integer", nullable: false),
                    series = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    turbine_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    hour_start_local = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    production_mwh = table.Column<double>(type: "double precision", nullable: true),
                    counter_kwh = table.Column<double>(type: "double precision", nullable: true),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    import_batch_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hourly_productions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_batches",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    plant_id = table.Column<int>(type: "integer", nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    file_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    detected_year = table.Column<int>(type: "integer", nullable: true),
                    detected_month = table.Column<int>(type: "integer", nullable: true),
                    month_mismatch = table.Column<bool>(type: "boolean", nullable: false),
                    warnings = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    row_count = table.Column<int>(type: "integer", nullable: false),
                    imported_at_utc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    imported_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ingest_batches",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    plant_id = table.Column<int>(type: "integer", nullable: false),
                    reader_local_time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    cycle = table.Column<int>(type: "integer", nullable: false),
                    server = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    read_method = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    reader_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    read_count = table.Column<int>(type: "integer", nullable: false),
                    good_count = table.Column<int>(type: "integer", nullable: false),
                    stored_count = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    clock_fallback = table.Column<bool>(type: "boolean", nullable: false),
                    received_at_utc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ingest_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "latest_readings",
                columns: table => new
                {
                    signal_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    plant_id = table.Column<int>(type: "integer", nullable: false),
                    turbine_id = table.Column<int>(type: "integer", nullable: true),
                    numeric_value = table.Column<double>(type: "double precision", nullable: true),
                    text_value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    quality_raw = table.Column<int>(type: "integer", nullable: false),
                    is_good = table.Column<bool>(type: "boolean", nullable: false),
                    captured_at_local = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    source_timestamp_local = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    batch_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_latest_readings", x => x.signal_id);
                });

            migrationBuilder.CreateTable(
                name: "plants",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    location = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    technology = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    turbine_count = table.Column<int>(type: "integer", nullable: true),
                    sample_only = table.Column<bool>(type: "boolean", nullable: false),
                    has_live_feed = table.Column<bool>(type: "boolean", nullable: false),
                    opc_prefix = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reading_raws",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    batch_id = table.Column<long>(type: "bigint", nullable: false),
                    plant_id = table.Column<int>(type: "integer", nullable: false),
                    signal_id = table.Column<int>(type: "integer", nullable: false),
                    turbine_id = table.Column<int>(type: "integer", nullable: true),
                    captured_at_local = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    source_timestamp_local = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    numeric_value = table.Column<double>(type: "double precision", nullable: true),
                    text_value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    quality_raw = table.Column<int>(type: "integer", nullable: false),
                    is_good = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reading_raws", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reading_ten_mins",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    plant_id = table.Column<int>(type: "integer", nullable: false),
                    signal_id = table.Column<int>(type: "integer", nullable: false),
                    turbine_id = table.Column<int>(type: "integer", nullable: true),
                    bucket_start_local = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    avg_value = table.Column<double>(type: "double precision", nullable: true),
                    min_value = table.Column<double>(type: "double precision", nullable: true),
                    max_value = table.Column<double>(type: "double precision", nullable: true),
                    sample_count = table.Column<int>(type: "integer", nullable: false),
                    good_count = table.Column<int>(type: "integer", nullable: false),
                    running_samples = table.Column<int>(type: "integer", nullable: false),
                    maintenance_samples = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reading_ten_mins", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shift_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    plant_id = table.Column<int>(type: "integer", nullable: false),
                    turbine_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    occurred_at_local = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    author_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shift_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "signals",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    plant_id = table.Column<int>(type: "integer", nullable: false),
                    turbine_id = table.Column<int>(type: "integer", nullable: true),
                    item_id = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    group_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    signal_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_signals", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    plant_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "turbines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    plant_id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    wtg_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_turbines", x => x.id);
                    table.ForeignKey(
                        name: "fk_turbines_plants_plant_id",
                        column: x => x.plant_id,
                        principalTable: "plants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_app_users_external_id",
                table: "app_users",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_faults_plant_id_occurred_at_local",
                table: "faults",
                columns: new[] { "plant_id", "occurred_at_local" });

            migrationBuilder.CreateIndex(
                name: "ix_hourly_productions_plant_id_hour_start_local",
                table: "hourly_productions",
                columns: new[] { "plant_id", "hour_start_local" });

            migrationBuilder.CreateIndex(
                name: "ix_hourly_productions_plant_id_series_turbine_code_hour_start_",
                table: "hourly_productions",
                columns: new[] { "plant_id", "series", "turbine_code", "hour_start_local" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_import_batches_plant_id_file_hash",
                table: "import_batches",
                columns: new[] { "plant_id", "file_hash" });

            migrationBuilder.CreateIndex(
                name: "ix_ingest_batches_plant_id_content_hash",
                table: "ingest_batches",
                columns: new[] { "plant_id", "content_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ingest_batches_plant_id_reader_local_time",
                table: "ingest_batches",
                columns: new[] { "plant_id", "reader_local_time" });

            migrationBuilder.CreateIndex(
                name: "ix_latest_readings_plant_id",
                table: "latest_readings",
                column: "plant_id");

            migrationBuilder.CreateIndex(
                name: "ix_plants_code",
                table: "plants",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reading_raws_captured_at_local",
                table: "reading_raws",
                column: "captured_at_local");

            migrationBuilder.CreateIndex(
                name: "ix_reading_raws_plant_id_signal_id_captured_at_local",
                table: "reading_raws",
                columns: new[] { "plant_id", "signal_id", "captured_at_local" });

            migrationBuilder.CreateIndex(
                name: "ix_reading_ten_mins_plant_id_signal_id_bucket_start_local",
                table: "reading_ten_mins",
                columns: new[] { "plant_id", "signal_id", "bucket_start_local" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shift_logs_plant_id_occurred_at_local",
                table: "shift_logs",
                columns: new[] { "plant_id", "occurred_at_local" });

            migrationBuilder.CreateIndex(
                name: "ix_signals_plant_id_item_id",
                table: "signals",
                columns: new[] { "plant_id", "item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_turbines_plant_id_code",
                table: "turbines",
                columns: new[] { "plant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_user_id_role_plant_code",
                table: "user_roles",
                columns: new[] { "user_id", "role", "plant_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "faults");

            migrationBuilder.DropTable(
                name: "hourly_productions");

            migrationBuilder.DropTable(
                name: "import_batches");

            migrationBuilder.DropTable(
                name: "ingest_batches");

            migrationBuilder.DropTable(
                name: "latest_readings");

            migrationBuilder.DropTable(
                name: "reading_raws");

            migrationBuilder.DropTable(
                name: "reading_ten_mins");

            migrationBuilder.DropTable(
                name: "shift_logs");

            migrationBuilder.DropTable(
                name: "signals");

            migrationBuilder.DropTable(
                name: "turbines");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "plants");

            migrationBuilder.DropTable(
                name: "app_users");
        }
    }
}
