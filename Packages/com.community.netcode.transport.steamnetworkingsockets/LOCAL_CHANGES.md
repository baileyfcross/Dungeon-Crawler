Source revision: 0fab638470379ace12b0149dfb41c043d23dbce5.

Shutdown closes native sessions and disposes callbacks synchronously, then clears queued events. The upstream delayed coroutine could be cancelled when its scene-bound NetworkManager is destroyed. Original MIT license retained.

StartClient and StartServer report failure if Steam returns an invalid native handle, instead of reporting a successful start.

An optional IncomingConnectionAllowed predicate rejects non-members before accepting native sockets. Invalid incoming frame lengths are released and dropped before allocation.
