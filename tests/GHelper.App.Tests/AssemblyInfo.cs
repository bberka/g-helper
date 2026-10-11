using Xunit;

// Core keeps process wide static state (AppConfig, PlatformServices, Program.*); the tests swap fake platforms in and out.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
