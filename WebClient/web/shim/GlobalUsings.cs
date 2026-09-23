// Both the UI library and (implicitly) System.Threading define a Timer; the client means its own.
global using Timer = WaW.UiLib.Extra.Timer;
// OpenTK's GL enums include a 'Buffer' that would otherwise shadow System.Buffer.
global using Buffer = System.Buffer;
