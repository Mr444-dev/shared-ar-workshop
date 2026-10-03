# API serwera

Serwer używa wyłącznie Node.js Core i przechowuje pokoje w pamięci procesu.

```sh
node server.js --host 0.0.0.0 --port 8787
```

`GET /health` zwraca status i liczbę aktywnych pokoi, osób oraz obiektów. `POST /api/rooms/:code/join` przyjmuje `{"displayName":"Ada"}`. Kolejne zmiany są wysyłane przez `POST .../objects/upsert` z `clientId` i stanem klocka albo `POST .../objects/delete` z `clientId` i `objectId`. Klient pobiera zmiany przez `GET .../events?clientId=<id>&since=<numer>`; żądanie czeka do 20 sekund, chyba że wcześniej pojawi się zmiana.

Ograniczenia prototypu: maksymalnie 200 klocków, 32 członków i 40 operacji na sekundę na członka, retencja 12 godzin bez aktywności, historia zdarzeń 4096 wpisów. Kod pokoju daje prawo edycji. Nie ma kont, TLS, trwałego storage ani moderacji; publiczny hosting wymaga dodatkowej warstwy ochrony.
