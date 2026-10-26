using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataChat.Infrastructure.Persistence.Migrations
{
	/// <summary>
	/// The role questions run as (ADR 0001, layer 4): it cannot log in, owns nothing and is granted <c>SELECT</c> on each
	/// dataset table at import, nothing else. The app's user is made a member so it can <c>SET ROLE</c> to it. Roles
	/// are cluster-wide, hence the existence check: a second database on the same server reuses it.
	/// </summary>
	public partial class ReaderRole : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.Sql("""
				DO $$
				BEGIN
					IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'datachat_reader') THEN
						CREATE ROLE datachat_reader NOLOGIN NOINHERIT;
					END IF;
				END
				$$;
				GRANT datachat_reader TO CURRENT_USER;
				GRANT USAGE ON SCHEMA public TO datachat_reader;
				""");

			// Tables imported before this migration.
			migrationBuilder.Sql("""
				DO $$
				DECLARE dataset record;
				BEGIN
					FOR dataset IN SELECT table_name FROM datasets LOOP
						EXECUTE format('GRANT SELECT ON %I TO datachat_reader', dataset.table_name);
					END LOOP;
				END
				$$;
				""");
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.Sql("""
				DO $$
				DECLARE dataset record;
				BEGIN
					FOR dataset IN SELECT table_name FROM datasets LOOP
						EXECUTE format('REVOKE SELECT ON %I FROM datachat_reader', dataset.table_name);
					END LOOP;
				END
				$$;
				REVOKE USAGE ON SCHEMA public FROM datachat_reader;
				""");
		}
	}
}
