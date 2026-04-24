// Removed — ProjectManager was an abstraction without a real project
// concept behind it; it just held the track collection and proxied a
// reorder method. Ownership moved to TrackListViewModel, which IS the
// natural owner of the list it binds. Safe to git-rm.
