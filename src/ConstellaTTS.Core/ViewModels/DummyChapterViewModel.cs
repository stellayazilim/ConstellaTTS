// This file intentionally left as a tombstone.
//
// DummyChapterViewModel was a placeholder seeded into the chapter
// strip during early UI mockup work — fixed strings, no behaviour,
// no persistence. The persistence rebuild (see "ConstellaTTS Persist
// Turn.md") removed the chapter strip entirely; the view-model that
// owned the dummies (TrackListViewModel) no longer references this
// type, and no other call site does either.
//
// The file is kept (rather than deleted from the project) only to
// avoid touching the .csproj include list mid-rebuild. Once the
// persistence work has settled, this file should be removed
// outright.
namespace ConstellaTTS.Core.ViewModels;
