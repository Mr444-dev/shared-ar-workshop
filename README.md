# Współdzielony Warsztat AR

Mobilne MVP do wspólnego układania wirtualnych klocków w jednym fizycznym pomieszczeniu. Projekt jest flagowym pomysłem z briefu portfolio AR/XR: AR Foundation lokalizuje urządzenie i wykrywa podłogę, lokalna kotwica stabilizuje scenę, a lekki serwer HTTP z long-pollingiem rozsyła zmiany w pokoju.

## Zakres MVP

- Android z ARCore oraz iOS z ARKit przez AR Foundation.
- Ręczna kalibracja wspólnego układu odniesienia: obie osoby wskazują ten sam punkt początku i kierunek na podłodze.
- Wykrywanie poziomej płaszczyzny, kotwica pomieszczenia i umieszczanie klocków na siatce.
- Współdzielony pokój z kodem, dołączanie wielu użytkowników, tworzenie/aktualizowanie/usuwanie klocków.
- Kolizje, przełączanie klocków na fizykę dynamiczną, zapis i odczyt projektu jako JSON.
- Obracanie klocka skokowo o 90° i tryb usuwania przez dotknięcie obiektu.
- Panel diagnostyczny z FPS, stanem śledzenia, liczbą obiektów i opóźnieniem synchronizacji.
- Serwer Node.js bez zewnętrznych zależności; stan pokoi pozostaje w pamięci procesu.

Repo zawiera źródła aplikacji i serwera. W tym środowisku nie ma zainstalowanego Unity Editor, więc nie powstał APK/IPA, a scena nie została zaimportowana ani uruchomiona na urządzeniu. Instrukcja poniżej opisuje pierwsze uruchomienie i konfigurację XR.

## Wymagania

- Unity **6000.3.0f1 (Unity 6.3 LTS)**.
- Moduł Android Build Support z Android SDK/NDK i OpenJDK do budowania Androida.
- ARCore‑obsługiwane urządzenie z Androidem albo iPhone/iPad z obsługą ARKit. Budowanie i podpisywanie iOS wymaga macOS i Xcode.
- Node.js 20 lub nowszy do lokalnego serwera.
- Dwa urządzenia AR oraz wspólna widoczność tego samego, dobrze oświetlonego pomieszczenia do demonstracji synchronizacji.

## Uruchomienie serwera

```powershell
node server/server.js --host 0.0.0.0 --port 8787
```

Lokalne uruchomienie przydaje się do pracy nad backendem na komputerze. W aplikacji mobilnej używaj URL HTTPS publicznie wystawionego serwera; Android/iOS mogą blokować niezabezpieczone HTTP. Repo ma plik `render.yaml`, który pozwala utworzyć serwis Node z adresem HTTPS. Pokój jest przechowywany tylko w pamięci serwera i znika po restarcie.

Serwer nasłuchuje na `127.0.0.1` domyślnie. `0.0.0.0` jest potrzebne wyłącznie do testów z innych urządzeń w LAN. Nie wystawiaj go publicznie po HTTP: do zdalnego demo użyj HTTPS i hosta pod swoją kontrolą.

## Uruchomienie Unity

1. Otwórz folder `UnityProject` w Unity Hub. Przy pierwszym otwarciu skrypt Editor użyje Unity Package Manager API, aby dodać AR Foundation 6.3.1, ARCore XR Plugin 6.3.1, ARKit XR Plugin 6.3.1, Input System oraz uGUI. Nie edytuj ręcznie `Packages/manifest.json`.
2. W Unity otwórz **Project Settings → XR Plug-in Management**; jeżeli ustawienia targetów nie powstały automatycznie, zainicjalizuj je tam. Następnie wybierz **Tools → Shared Workshop → Configure XR providers**. Kreator włącza **ARCore** dla Androida i **ARKit** dla iOS.
3. Pierwsza instalacja pakietów automatycznie tworzy startową scenę z wizualizacją wykrytych płaszczyzn. Wybierz **Tools → Shared Workshop → Create starter scene**, aby odtworzyć scenę. Jest zapisywana w `Assets/Scenes/SharedWorkshop.unity` i dodawana do Build Settings.
4. W Build Settings wybierz Android lub iOS, zbuduj aplikację i uruchom ją na obsługiwanym urządzeniu.
5. W panelu aplikacji wpisz URL serwera, nazwę użytkownika i kod pokoju. Jedna osoba może wygenerować losowy kod. Pozostali wpisują ten sam kod.
6. Każdy uczestnik wybiera **Kalibruj**, wskazuje wspólny punkt początku na podłodze, a następnie wskazuje drugi punkt w tym samym kierunku. Po wyrównaniu układu można stawiać klocki.

Do stabilnego śledzenia skanuj szczegółowe powierzchnie przy dobrym świetle. Nie przesyłamy obrazu z kamery ani mapy pomieszczenia. Przez sieć idą wyłącznie kod pokoju, nazwa użytkownika, identyfikatory i transformacje wirtualnych klocków.

## API serwera

| Metoda | Endpoint | Działanie |
|---|---|---|
| `GET` | `/health` | Status serwera |
| `POST` | `/api/rooms/:code/join` | Dołączenie do pokoju i pełny zrzut stanu |
| `GET` | `/api/rooms/:code/events?since=:seq` | Long-polling zmian od sekwencji `seq` |
| `POST` | `/api/rooms/:code/objects/upsert` | Utworzenie lub zmiana klocka |
| `POST` | `/api/rooms/:code/objects/delete` | Usunięcie klocka |

Serwer waliduje kod pokoju, członkostwo, rozmiary wiadomości, limity współrzędnych, liczbę klocków oraz częstotliwość operacji. Prototyp nie ma kont ani trwałej bazy danych; kody pokoi działają jak zaproszenie. Render kończy HTTPS przed serwisem Node. Przed publicznym użyciem dodaj uwierzytelnianie, trwałe przechowywanie i kontrolę dostępu do pokoi.

## Struktura

```text
UnityProject/Assets/SharedWorkshop/Runtime/   # AR, kotwica, klocki, zapis, synchronizacja, HUD
UnityProject/Assets/Editor/                   # instalator pakietów, kreator sceny, ustawienia XR
server/server.js                              # API pokoi i long-polling, Node.js core only
docs/architecture.md                          # decyzje techniczne, współrzędne, bezpieczeństwo
docs/roadmap.md                               # zakres M0–M12 i pomiary do uzupełnienia
```

## CI

GitHub Actions uruchamia kontrolę składni serwera przy pushu. Osobny ręczny workflow buduje APK; wymaga Unity license w repozytoryjnych sekretach `UNITY_LICENSE`, `UNITY_EMAIL` i `UNITY_PASSWORD`. Workflow nie został uruchomiony w tym środowisku.

## Kryteria demonstracji

- Dwa urządzenia umieszczają klocki w tym samym miejscu po ręcznej kalibracji wspólnego układu.
- Rozłączenie i ponowne dołączenie do aktywnego pokoju odtwarza jego bieżący stan.
- Klocki kolidują lokalnie, a ruch dynamicznych klocków aktualizuje się u pozostałych klientów.
- Mierzone cele: co najmniej 30 FPS na urządzeniu referencyjnym, synchronizacja zwykle poniżej 250 ms w LAN i brak wysyłania obrazu z kamery.

Cele są wymaganiami demonstracyjnymi, nie wynikami pomiarów. Zarejestruj urządzenia, warunki oświetlenia, liczbę obiektów, średni FPS, utratę śledzenia oraz opóźnienie sieci przed publikacją nagrania demo.

## Plan rozwoju

MVP pokrywa wczesne kamienie milowe projektu A. Cloud Anchors/ARWorldMap, trwała baza danych, reconnection queue, obsługa awarii sieci, zaawansowana optymalizacja i pomiary dokładności pozostają kolejnymi zadaniami; szczegóły są w [`docs/roadmap.md`](docs/roadmap.md).
