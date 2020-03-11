## Developer notes

!! Be very careful when upstanding NuGet packages !! 
We are pushing here the limits on the VS extensibility SDK and it's very easy to break things. In particular the most dreadful symptom that should be checked on each package update is the ability to start a debug session.
On failure usually a CompositionException is silently thrown and the debugger refuses to start without any further clue on what is wrong.
