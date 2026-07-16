using System;

namespace Klass
{
    public class Lag
    {
        public string Namn { get; set; } = "";
        public string HemmaHall { get; set; } = "";
        public string Serie { get; set; } = "";
        public string KM { get; set; } = "";
        public string UserID { get; set; } = "";
        public string LagID { get; set; } = "";
    }
    public class HA
    { 
    public string HändelseID { get; set; } = "";
    public string Händelse { get; set; } = "";

}

public class Handelser
    {
        public string MatchID { get; set; } = "";
        public string SpelareID { get; set; } = "";
        public string Tid { get; set; } = "";
        public string HändelseID { get; set; } = "";
    }
    public class HandelseLista
    {
        public string Text { get; set; } = "";
        public string HändelseID { get; set; } = "";
    }

    public class User
    {
        public string UserID { get; set; } = "";
        public string UserName { get; set; } = "";
        public string Password { get; set; } = "";
        public string typ { get; set; } = "";
        public string status { get; set; } = "";
    }
    public class Match
    {
        public string LagID { get; set; } = "";
        public string MatchID { get; set; } = "";
        public string Datum { get; set; } = "";
        public string Motståndare { get; set; } = "";
        public string Plats { get; set; } = "";
        public string Status { get; set; } = "";
    }
    public class Händelse
    {
        public string Händelsen { get; set; } = "";
        public string HändelseID { get; set; } = "";
        public string MatchID { get; set; } = "";
        public string SpelareID { get; set; } = "";
        public string Zon { get; set; } = "";
        public string Tids { get; set; } = "";
        public string Fas { get; set; } = "";
    }

    public class Events
    {
        public string Tid { get; set; } = "";
        public string Tids { get; set; } = "";
        public string namn { get; set; } = "";
        public string Händelse { get; set; } = "";
        public string SpelarID { get; set; } = "";
    }

    public class EventsTyp
    {
        public string Tid { get; set; } = "";
        public string namn { get; set; } = "";
        public string Typ { get; set; } = "";
        public string Händelse { get; set; } = "";
        public string TeknisktFel { get; set; } = "";
        public string Avslut { get; set; } = "";
        public string Mål { get; set; } = "";
        public string Zon { get; set; } = "";
    }

    public class EventsSam
    {
        public string namn { get; set; } = "";
        public string TekniskaFel { get; set; } = "";
        public string Avslut { get; set; } = "";
        public string Mål { get; set; } = "";
        public string Målperavslut { get; set; } = "";
        public string Position { get; set; } = "";
    }


    public class Malvakt
    {
        public string Raddningar { get; set; } = "";
        public string Mål { get; set; } = "";
        public string Procent { get; set; } = "";
    }
    public class Malvakt2
    {
        public string Namn { get; set; } = "";
        public string Raddningar { get; set; } = "";
        public string Mål { get; set; } = "";
        public string Fel { get; set; } = "";

        public string Procent { get; set; } = "";
    }

    public class Events2
    {
        public string Tid { get; set; } = "";
        public string namn { get; set; } = "";
        public string Händelse { get; set; } = "";
    }

    public class antH
    {
        public string Namn { get; set; } = "";
        public string antHa { get; set; } = "";
    }

    public class antM
    {
        public string Namn { get; set; } = "";
        public string antMa { get; set; } = "";
    }
    public class Spelare
    {
        public string SpelareID { get; set; } = "";
        public string Efternamn { get; set; } = "";
        public string Förnamn { get; set; } = "";
        public string LagID { get; set; } = "";
        public string Position { get; set; } = "";
        public string Nummer { get; set; } = "";
        public string XNummer { get; set; } = "";
    }
    public class Sp
    {
        public string SpelareID { get; set; } = "";
        public string Nummer { get; set; } = "";
    }
}
