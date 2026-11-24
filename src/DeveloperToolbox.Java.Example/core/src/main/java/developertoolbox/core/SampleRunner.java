package developertoolbox.core;

/** Injectable boundary shared by the command line and HTTP Functions. */
@FunctionalInterface
public interface SampleRunner { String run(String resource) throws Exception; }
