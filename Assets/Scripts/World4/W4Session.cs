// Minimal session holder so Cards & PopUpTrigger share the chosen letter
public static class W4Session
{
    public static string CurrentLetter; // set in PopUpTrigger, read by Cards
}