package developertoolbox.core;

import java.sql.DriverManager;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import org.hibernate.SessionFactory;
import org.hibernate.boot.MetadataSources;
import org.hibernate.boot.registry.StandardServiceRegistryBuilder;
import org.hibernate.exception.ConstraintViolationException;

/** Hibernate ORM transactions against dedicated local SQL Server and PostgreSQL databases. */
public final class DatabaseSamples implements MessageStore, AutoCloseable {
    private final Settings settings;
    private final Map<String, SessionFactory> factories = new ConcurrentHashMap<>();
    public DatabaseSamples(Settings settings) { this.settings = settings; }
    public DatabaseSamples(SessionFactory factory) { settings = null; factories.put("test", factory); }
    @Override public boolean save(String database, WorkItem work, String source) {
        java.util.Objects.requireNonNull(work);
        if (source == null || source.isBlank() || source.length() > 64) throw new IllegalArgumentException("A bounded source is required.");
        var factory = factories.computeIfAbsent(database, this::createFactory);
        try (var session = factory.openSession()) {
            var transaction = session.getTransaction();
            transaction.setTimeout(20);
            transaction.begin();
            try {
                if (session.find(SampleMessage.class, work.id()) != null) { transaction.commit(); return false; }
                session.persist(new SampleMessage(work, source));
                transaction.commit();
                return true;
            } catch (RuntimeException ex) {
                if (transaction.isActive()) transaction.rollback();
                if (duplicateKey(ex)) {
                    try (var read = factory.openSession()) {
                        if (read.find(SampleMessage.class, work.id()) != null) return false;
                    }
                }
                throw ex;
            }
        }
    }
    public String demonstrate(String database) {
        var work = WorkItem.sample();
        if (!save(database, work, "java-cli")) throw new IllegalStateException("Expected a new message.");
        var factory = factories.get(database);
        try (var session = factory.openSession()) {
            var rows = session.createQuery("from SampleMessage where id = :id", SampleMessage.class)
                .setParameter("id", work.id()).getResultList();
            if (rows.size() != 1 || !rows.getFirst().getText().equals(work.text())) throw new IllegalStateException("ORM roundtrip failed.");
            var transaction = session.beginTransaction();
            var rolledBack = WorkItem.sample();
            session.persist(new SampleMessage(rolledBack, "java-rollback"));
            session.flush(); transaction.rollback(); session.clear();
            if (session.find(SampleMessage.class, rolledBack.id()) != null) throw new IllegalStateException("Rollback failed.");
        }
        return "Java ORM insert, parameterized query and rollback succeeded.";
    }
    private SessionFactory createFactory(String database) {
        if (!java.util.Set.of("sqlserver", "postgres").contains(database)) throw new IllegalArgumentException("Unknown database.");
        String host, user, password, adminUrl, sampleUrl;
        if (database.equals("sqlserver")) {
            host = settings.get("JAVA_SQLSERVER_HOST", "127.0.0.1"); Settings.localHost(host, "mssql");
            user = settings.get("JAVA_SQLSERVER_USER", "sa"); password = settings.require("MSSQL_SA_PASSWORD");
            var base = "jdbc:sqlserver://" + host + ":" + settings.port("JAVA_SQLSERVER_PORT", 10433) + ";encrypt=true;trustServerCertificate=true;loginTimeout=10;socketTimeout=20000;databaseName=";
            adminUrl = base + "master"; sampleUrl = base + "ToolboxJavaSamples";
        } else {
            host = settings.get("JAVA_POSTGRES_HOST", "localhost"); Settings.localHost(host, "postgres");
            user = settings.require("POSTGRES_USER"); password = settings.require("POSTGRES_PASSWORD");
            var base = "jdbc:postgresql://" + host + ":" + settings.port("JAVA_POSTGRES_PORT", 10432) + "/";
            var suffix = "?connectTimeout=10&socketTimeout=20";
            adminUrl = base + "postgres" + suffix; sampleUrl = base + "ToolboxJavaSamples" + suffix;
        }
        try (var connection = DriverManager.getConnection(adminUrl, user, password)) {
            if (database.equals("sqlserver")) {
                try (var statement = connection.createStatement()) { statement.execute("IF DB_ID(N'ToolboxJavaSamples') IS NULL CREATE DATABASE [ToolboxJavaSamples]"); }
            } else {
                try (var query = connection.prepareStatement("SELECT 1 FROM pg_database WHERE datname = ?")) {
                    query.setString(1, "ToolboxJavaSamples");
                    try (var rows = query.executeQuery()) {
                        if (!rows.next()) try (var statement = connection.createStatement()) { statement.execute("CREATE DATABASE \"ToolboxJavaSamples\""); }
                    }
                }
            }
        } catch (java.sql.SQLException ex) {
            // Concurrent process startup may create the dedicated database first.
            if (!("42P04".equals(ex.getSQLState()) || ex.getErrorCode() == 1801)) throw new IllegalStateException("Cannot initialize local Java database.", ex);
        }
        return factory(Map.of("hibernate.connection.url", sampleUrl, "hibernate.connection.username", user,
            "hibernate.connection.password", password, "hibernate.hbm2ddl.auto", "update",
            "hibernate.show_sql", "false", "hibernate.connection.pool_size", "5"));
    }
    public static SessionFactory factory(Map<String, Object> properties) {
        var registry = new StandardServiceRegistryBuilder().applySettings(properties).build();
        try { return metadata(registry).buildSessionFactory(); }
        catch (RuntimeException ex) { StandardServiceRegistryBuilder.destroy(registry); throw ex; }
    }
    static org.hibernate.boot.Metadata metadata(org.hibernate.boot.registry.StandardServiceRegistry registry) {
        return new MetadataSources(registry).addAnnotatedClass(SampleMessage.class).buildMetadata();
    }
    private static boolean duplicateKey(Throwable ex) {
        for (var cause = ex; cause != null; cause = cause.getCause()) {
            if (cause instanceof ConstraintViolationException constraint) {
                var sql = constraint.getSQLException();
                return "23505".equals(sql.getSQLState()) || sql.getErrorCode() == 2627 || sql.getErrorCode() == 2601;
            }
        }
        return false;
    }
    @Override public void close() { factories.values().forEach(SessionFactory::close); }
}
