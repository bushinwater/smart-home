# MQTT ugovor (v0.3)

Ugovor između ESP32 (pravog ili simulatora) i backenda. Ko god poštuje ovaj dokument, može da zameni drugu stranu.

## 1. Opšta pravila

- Svi topic-i počinju sa `home/`.
- Struktura: `home/<modul>/<stvar>/<tip>`, a gde nema podstvari, samo `home/<modul>/<tip>`.
- Poslednji segment govori kakva je poruka:

| Poslednji segment | Smer | `retained` | QoS | Značenje |
|---|---|---|---|---|
| `state` | ESP32 → backend | **da** | 0 | Trenutno stanje. Novi pretplatnik ga odmah dobija. |
| `event` | ESP32 → backend | **nikad** | 1 | Nešto se desilo (jednom). |
| glagol (`set`, `unlock`, `send`) | backend → ESP32 | **nikad** | 1 | Komanda. |
| `status` | ESP32 → backend | **da** | 1 | Online/offline (Last Will). |

- Payload je JSON, ključevi su `camelCase`, enum vrednosti su mala slova (`"manual"`, `"cool"`).
- Jedinice: temperatura u °C, vlažnost u % (decimalni brojevi).
- ESP32 **ne šalje vreme**. Backend upisuje vreme prijema. Zbog toga ESP32 ne zavisi od NTP-a, pa ni od interneta.
- ESP32 se povezuje sa **clean session**. Komande koje stignu dok je offline broker ne čuva, pa se ne izvrše kasnije. Ovo je ključno za bravu.

## 2. Status uređaja

`home/devices/<deviceId>/status`: retained, QoS 1. Jedini uređaj je `esp32-main`. Na njemu su sve funkcije, uključujući i sanduče.

Posle povezivanja:
```json
{ "online": true, "firmware": "0.1.0" }
```

Last Will (broker je sam objavi kad veza pukne):
```json
{ "online": false }
```

## 3. Senzori

Objavljuju se periodično (svakih 30 s) i odmah posle povezivanja.

`home/sensors/indoor/state`:
```json
{ "temperature": 22.4, "humidity": 45.1 }
```

`home/sensors/outdoor/state`:
```json
{ "temperature": 3.5 }
```

## 4. Grejanje

Logika (histereza, zadata temperatura, režim) živi na ESP32. Backend samo zadaje i prikazuje.

`home/heating/state` (ESP32 → backend, posle svake promene i odmah posle povezivanja):
```json
{ "mode": "manual", "targetTemperature": 21.5, "boilerOn": true }
```
- `mode`: `"off"` | `"manual"` | `"schedule"`
- `boilerOn`: da li je relej kotla trenutno uključen.

`home/heating/set` (backend → ESP32). Sva polja su opciona, a šalje se samo ono što se menja:
```json
{ "mode": "manual", "targetTemperature": 21.5 }
```
- ESP32 prihvata `targetTemperature` samo u opsegu 5–30 °C. Nevažeću komandu ignoriše.
- Posle svake komande ESP32 objavi `home/heating/state`, pa backend tako zna da je komanda primljena.
- Raspored (`schedule`) se definiše kad budemo radili modul grejanja.

## 5. Brava

`home/lock/unlock` (backend → ESP32):
```json
{ "requestId": "3f2a9c1e", "source": "panel" }
```
- `source`: `"panel"` | `"mobile"`
- **Nema trajanja impulsa.** Njega određuje firmver.
- ESP32 pamti poslednji `requestId` i ignoriše duplikat (QoS 1 može da isporuči poruku dvaput).

`home/lock/event` (ESP32 → backend):
```json
{ "type": "unlocked", "source": "panel", "requestId": "3f2a9c1e" }
{ "type": "unlocked", "source": "button" }
{ "type": "locked" }
```
- `source: "button"`: fizičko dugme. ESP32 je otključao lokalno, a backendu samo javlja. `requestId` tada ne postoji.
- `locked` se šalje kad se impuls završi.
- Backend na osnovu `requestId` zna **ko** je tražio otključavanje i upisuje to u log.

## 6. Klima (IR)

IR je jednosmeran, pa ESP32 ne zna stvarno stanje klime, već samo šta je poslao.

`home/climate/send` (backend → ESP32). Uvek se šalje **puno stanje**, jer i daljinski klime tako radi:
```json
{ "power": true, "mode": "cool", "temperature": 24, "fan": "auto" }
```
- `mode`: `"cool"` | `"heat"` | `"dry"` | `"fan"` | `"auto"`
- `fan`: `"auto"` | `"low"` | `"medium"` | `"high"`

`home/climate/state` (ESP32 → backend, posle slanja IR signala), sa istim oblikom kao komanda:
```json
{ "power": true, "mode": "cool", "temperature": 24, "fan": "auto" }
```

## 7. Poštansko sanduče

Sanduče je pored ulaznih vrata, a senzor je žicom povezan na `esp32-main` (istim kablom kao brava i DS18B20). Ugovor ne zavisi od vrste senzora (prekidač, senzor razdaljine…). ESP32 samo javlja da li u sandučetu ima nečega.

`home/mailbox/state` (ESP32 → backend, odmah posle povezivanja i pri svakoj promeni):
```json
{ "hasMail": true }
```

`home/mailbox/event` (ESP32 → backend, samo kad se `hasMail` promeni):
```json
{ "type": "mailArrived" }
{ "type": "mailCollected" }
```
- `state` služi za prikaz na panelu (retained, pa backend zna stanje i posle restarta).
- `event` služi za notifikaciju na panel i telefon. Šalje se tačno jednom po promeni.
- ESP32 filtrira treperenje senzora (debounce), npr. promena važi tek kad je stabilna 1–2 s. Tako jedno ubacivanje pisma ne pravi niz `mailArrived`/`mailCollected`.

## 8. Fizička dugmad na panelu

Dugme za otključavanje je opisano u sekciji 5 (`home/lock/event`, `source: "button"`).

`home/panel/button/event` (ESP32 → backend):
```json
{ "button": "talk" }
```

## 9. Pristup brokeru (ACL)

Svaki korisnik sme da objavljuje **samo ono što mu pripada**. Zato se topic-i navode pojedinačno, a ne preko džokera kao `home/+/state`. Takav džoker bi uređaju dozvolio i topic-e koje mu ne pripadaju. Na primer, kad se doda nov uređaj, stari bi mogao da ga glumi.

| Korisnik | Sme da objavljuje (publish) | Sme da se pretplati (subscribe) |
|---|---|---|
| `backend` | `home/heating/set`, `home/lock/unlock`, `home/climate/send` | `home/#` |
| `esp32-main` | `home/devices/esp32-main/status`, `home/sensors/+/state`, `home/heating/state`, `home/lock/event`, `home/climate/state`, `home/mailbox/state`, `home/mailbox/event`, `home/panel/button/event` | `home/heating/set`, `home/lock/unlock`, `home/climate/send` |

Samo `backend` sme da pošalje `unlock`. Backend pre slanja proverava ko traži otključavanje.
