using Xunit;

// These tests read and change the real machine (power plan, settings, temp files). Two of them
// touching the same thing at once gives false failures, so they run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
