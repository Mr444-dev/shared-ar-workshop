# Zakres względem kamieni milowych M0–M12

Oznaczenia mówią, co znajduje się w repozytorium. Żaden punkt wymagający uruchomienia Unity lub telefonu nie jest przedstawiany jako zweryfikowany.

| Etap | Zakres w projekcie | Stan |
|---|---|---|
| M0 | Struktura Unity/serwera, stos AR Foundation i dokumentacja architektury | Kod i konfiguracja przygotowane; Unity nie jest zainstalowane w środowisku |
| M1 | AR Session, kamera, śledzenie pozycji i detekcja poziomej podłogi | Kreator sceny i kod są w repo; nieuruchomione na urządzeniu |
| M2 | Kotwica pokoju, ręczna kalibracja dwóch punktów, klocki i siatka 12 cm | Zaimplementowane w źródłach; wymaga weryfikacji w Unity |
| M3 | Kolizje, grawitacja i statyczna podłoga; limit 8 dynamicznych klocków | Zaimplementowane w źródłach; brak profilu CPU/GPU |
| M4 | Pokoje, snapshot, sekwencje zmian i synchronizacja przez HTTP long-polling | Serwer i klient są w repo; brak pomiaru dwóch urządzeń |
| M5 | Wspólne pozycje przez ręcznie powtarzaną kalibrację | Jest prototyp; Cloud Anchors i ARWorldMap pozostają poza MVP |
| M6 | Wybór koloru, usuwanie, zapis/odczyt JSON po stronie klienta | Zaimplementowane w źródłach; wymaga próby na telefonie |
| M7 | Ponawianie polling, resynchronizacja snapshotem i ponowne dołączanie | Częściowo: retry i snapshot są; brak kolejki offline i kontrolowanych testów awarii |
| M8 | HUD z FPS/RTT/tracking, limity pracy i 200-obiektowy limit pokoju | Instrumentacja jest; brak benchmarku, LOD i profilowania urządzeń |
| M9 | Synchronizacja ruchu fizycznego w ograniczonym zakresie | Prototyp, nie deterministyczna symulacja; brak testu rozjazdu klientów |
| M10 | Powtarzalne pomiary błędu pozycji, driftu i odzyskiwania trackingu | Do wykonania na urządzeniach z oznaczonymi punktami pomiarowymi |
| M11 | README, architektura, serwer i workflow CI | Dokumentacja źródła jest; nagranie demo i screenshoty pozostają do wykonania |
| M12 | Wnioski, KPI z testów i plan rozbudowy | Po zebraniu rzeczywistych wyników |

## Kolejność następnych prac

1. Otworzyć projekt w Unity 6.3, poczekać na rozwiązanie pakietów i wygenerowanie sceny, włączyć ARCore/ARKit, a następnie poprawić ewentualne błędy kompilacji w edytorze.
2. Zbudować Android APK i sprawdzić uprawnienie kamery, tracking oraz działanie kotwicy przy utracie śledzenia.
3. Wystawić serwer przez HTTPS i wykonać wspólną sesję na dwóch urządzeniach; zmierzyć offset ręcznej kalibracji oraz RTT.
4. Dodać providera współdzielonych kotwic, trwały storage, testy sieci z opóźnieniem/utratą pakietów oraz automatyczne buildy po skonfigurowaniu licencji Unity.
5. Zmierzyć FPS/CPU/GPU/pamięć/baterię i wstawić wyłącznie zmierzone wyniki do README oraz nagrania demo.
