# KC Tools lisans veritabanı — rol ayrımı
# Bu dosya setup-roles.ps1 tarafından geçici bir kopyaya dönüştürülür.
# Parolalar kaynak kodda TUTULMAZ; placeholder'lar çalışma anında doldurulur.

-- Migrator rolü (DDL + migration sahibi)
SELECT 'CREATE ROLE kctools_migrator LOGIN PASSWORD ' || quote_literal('__MIGRATOR_PASSWORD__')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'kctools_migrator') \gexec

ALTER ROLE kctools_migrator WITH LOGIN PASSWORD '__MIGRATOR_PASSWORD__';

-- Uygulama rolü (yalnızca DML)
SELECT 'CREATE ROLE kctools_app LOGIN PASSWORD ' || quote_literal('__APP_PASSWORD__')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'kctools_app') \gexec

ALTER ROLE kctools_app WITH LOGIN PASSWORD '__APP_PASSWORD__';

-- Veritabanı sahipliği ve bağlantı izinleri
ALTER DATABASE "__DATABASE__" OWNER TO kctools_migrator;
GRANT CONNECT ON DATABASE "__DATABASE__" TO kctools_migrator;
GRANT CONNECT ON DATABASE "__DATABASE__" TO kctools_app;

-- Şema erişimi
GRANT USAGE ON SCHEMA public TO kctools_app;
GRANT CREATE, USAGE ON SCHEMA public TO kctools_migrator;

-- Mevcut tablolar için DML
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO kctools_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO kctools_app;

-- Gelecekte migrator'ın oluşturacağı tablolar için varsayılan izinler
ALTER DEFAULT PRIVILEGES FOR ROLE kctools_migrator IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO kctools_app;

ALTER DEFAULT PRIVILEGES FOR ROLE kctools_migrator IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO kctools_app;
