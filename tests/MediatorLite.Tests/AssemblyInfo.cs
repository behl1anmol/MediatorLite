// Fixtures in SourceGeneration/TestTypes.cs track invocations through mutable statics
// (rule 70 §6), and the open-generic behaviors (GenericLoggingBehavior,
// ExecutionOrderTrackingBehavior) funnel *every* request from *every* test class through
// the same static lists. xUnit runs test classes in parallel by default, so those lists
// were racing across classes (concurrent List<T>.Add — an intermittent CI flake).
// Serialize the whole assembly until the fixtures stop sharing static state.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
