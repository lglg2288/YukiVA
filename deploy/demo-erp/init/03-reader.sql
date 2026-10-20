-- Роль только на чтение — под ней ходит MCP-сервер, LLM не сможет ничего изменить.
create role erp_reader login password 'erp_reader_password';

grant connect on database erp to erp_reader;
grant usage on schema public to erp_reader;
grant select on all tables in schema public to erp_reader;
alter default privileges in schema public grant select on tables to erp_reader;

-- Бесконечный запрос от модели не повесит базу.
alter role erp_reader set statement_timeout = '10s';
alter role erp_reader set default_transaction_read_only = on;
