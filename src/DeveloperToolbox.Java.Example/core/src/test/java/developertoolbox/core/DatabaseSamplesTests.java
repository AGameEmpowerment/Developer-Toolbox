package developertoolbox.core;

import org.hibernate.SessionFactory;
import org.junit.jupiter.api.Test;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.Executors;
import static org.junit.jupiter.api.Assertions.*;

class DatabaseSamplesTests {
    @Test void sqlServerPreservesUnicodeIdentifiersAndMessages() throws java.sql.SQLException {
        assertCharacterTypes("org.hibernate.dialect.SQLServerDialect", "nvarchar");
    }
    @Test void postgresUsesItsUnicodeCapableCharacterType() throws java.sql.SQLException {
        assertCharacterTypes("org.hibernate.dialect.PostgreSQLDialect", "varchar");
    }
    private void assertCharacterTypes(String dialect, String expected) throws java.sql.SQLException {
        var registry = new org.hibernate.boot.registry.StandardServiceRegistryBuilder()
            .applySetting("hibernate.dialect", dialect)
            .applySetting("hibernate.boot.allow_jdbc_metadata_access", false).build();
        try {
            var metadata = DatabaseSamples.metadata(registry);
            var entity = metadata.getEntityBinding(SampleMessage.class.getName());
            for (var property : java.util.List.of(entity.getIdentifierProperty(), entity.getProperty("text"))) {
                var type = property.getColumns().getFirst().getSqlType(metadata);
                assertTrue(type.startsWith(expected), () -> property.getName() + " maps to " + type);
                var calls = new java.util.ArrayList<String>();
                var statement = (java.sql.PreparedStatement) java.lang.reflect.Proxy.newProxyInstance(getClass().getClassLoader(),
                    new Class<?>[]{java.sql.PreparedStatement.class}, (proxy, method, arguments) -> {
                        calls.add(method.getName());
                        assertEquals("hello 🌎", arguments[1]);
                        return null;
                    });
                var options = (org.hibernate.type.descriptor.WrapperOptions) java.lang.reflect.Proxy.newProxyInstance(getClass().getClassLoader(),
                    new Class<?>[]{org.hibernate.type.descriptor.WrapperOptions.class}, (proxy, method, arguments) ->
                        method.getName().equals("getDialect") ? registry.getService(org.hibernate.engine.jdbc.spi.JdbcServices.class).getDialect() : null);
                @SuppressWarnings("unchecked") var basic = (org.hibernate.type.BasicType<String>) property.getType();
                basic.getJdbcValueBinder().bind(statement, "hello 🌎", 1, options);
                assertEquals(java.util.List.of(expected.equals("nvarchar") ? "setNString" : "setString"), calls);
            }
        } finally { org.hibernate.boot.registry.StandardServiceRegistryBuilder.destroy(registry); }
    }
    private SessionFactory factory() {
        return DatabaseSamples.factory(Map.of("hibernate.connection.url", "jdbc:h2:mem:" + UUID.randomUUID() + ";DB_CLOSE_DELAY=-1",
            "hibernate.hbm2ddl.auto", "create-drop", "hibernate.show_sql", "false"));
    }
    @Test void committedMessagesAreIdempotentAndRetainOriginalText() {
        var factory = factory();
        try (var database = new DatabaseSamples(factory)) {
            assertTrue(database.save("test", new WorkItem("id", "first"), "test"));
            assertFalse(database.save("test", new WorkItem("id", "replacement"), "test"));
            try (var session = factory.openSession()) { assertEquals("first", session.find(SampleMessage.class, "id").getText()); }
        }
    }
    @Test void schemaDemoExercisesCommittedInsertParameterizedQueryAndRollback() {
        try (var database = new DatabaseSamples(factory())) { assertTrue(database.demonstrate("test").contains("rollback succeeded")); }
    }
    @Test void failedWriteRollsBackAndPropagates() {
        var factory = factory();
        try (var database = new DatabaseSamples(factory)) {
            try (var session = factory.openSession()) {
                session.doWork(connection -> { try (var statement = connection.createStatement()) { statement.execute("ALTER TABLE JavaMessages ADD CONSTRAINT reject_bad CHECK (text <> 'bad')"); } });
            }
            assertThrows(RuntimeException.class, () -> database.save("test", new WorkItem("id", "bad"), "test"));
            assertTrue(database.save("test", new WorkItem("id", "good"), "test"));
        }
    }
    @Test void concurrentDuplicateDeliveryCommitsExactlyOnce() throws Exception {
        try (var database = new DatabaseSamples(factory()); var executor = Executors.newFixedThreadPool(4)) {
            var tasks = java.util.stream.IntStream.range(0, 4).mapToObj(i -> (java.util.concurrent.Callable<Boolean>) () -> database.save("test", new WorkItem("duplicate", "same"), "test")).toList();
            var results = executor.invokeAll(tasks);
            int inserted = 0; for (var result : results) if (result.get()) inserted++;
            assertEquals(1, inserted);
        }
    }
    @Test void invalidSourceHasNoDatabaseSideEffects() {
        try (var database = new DatabaseSamples(new Settings(Map.of()))) {
            var error = assertThrows(IllegalArgumentException.class, () -> database.save("postgres", WorkItem.sample(), "x".repeat(65)));
            assertEquals("A bounded source is required.", error.getMessage());
        }
    }
}
