package developertoolbox.core;

/** Queue acknowledgments follow a committed, idempotent database write. */
@FunctionalInterface
public interface MessageStore {
    boolean save(String database, WorkItem work, String source);
}
