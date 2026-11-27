# OpenShift'e Dağıtım

## Önce ağ tarafını kontrol edin (kod bunu çözemez)

- OpenShift cluster'ından SQL Server'a (1433 ya da kurulu port) ağ erişimi olmalı.
- OpenShift cluster'ından `fintek.local:636` (LDAPS) erişimi olmalı, DNS çözümlemesi dahil.
- Bunlardan biri kapalıysa uygulama ayağa kalkar ama `/healthz/ready` (DB) veya login
  (LDAP) başarısız olur — hata görünce önce burayı kontrol edin.

## Image'leri build edip registry'ye gönderme

Kendi CI/CD'niz varsa `Dockerfile` (Web) ve
`src/ITInventory.ExpirationNotifier/Dockerfile` (Notifier) dosyalarını build edip
registry'nize push edin. OpenShift'in kendisine build ettirmek isterseniz
`imagestream-buildconfig.yaml`'daki notlara bakın.

```
docker build -t <registry>/itinventory-web:latest -f Dockerfile .
docker build -t <registry>/itinventory-notifier:latest -f src/ITInventory.ExpirationNotifier/Dockerfile .
docker push <registry>/itinventory-web:latest
docker push <registry>/itinventory-notifier:latest
```

## Uygulama sırası

1. `secret.example.yaml`'ı **kopyalamadan**, gerçek değerleri doğrudan cluster'a
   `oc create secret` ile yazın (dosyadaki komut örneğine bakın) — connection string ve
   SMTP parolası git'e girmemeli.
2. `oc apply -f configmap-web.yaml -f configmap-notifier.yaml`
3. `oc apply -f pvc-dataprotection-keys.yaml`
4. `deployment-web.yaml`, `deployment-notifier.yaml`, `route-web.yaml` içindeki
   `CHANGE_ME` alanlarını (image adresi, Route host'u, BuildConfig branch'i) doldurun.
5. `oc apply -f deployment-web.yaml -f service-web.yaml -f route-web.yaml -f deployment-notifier.yaml`

## Doğrulama

```
oc get pods -w
oc exec deploy/itinventory-web -- curl -sf localhost:8080/healthz/ready
curl -sf https://<route-host>/healthz/live
```

`itinventory-web` pod'u `Running` + `1/1 Ready` olmalı. `/healthz/ready` 200 dönmüyorsa
DB bağlantısını (Secret'taki connection string, ağ erişimi) kontrol edin.

## Prod'a almadan önce

- `configmap-web.yaml`'da `TestLogin__Enabled: "false"` — bu zaten varsayılan, elle
  `"true"` yapmayın (paylaşılan tek şifreyle giriş açılır).
- `itinventory-web`'i tek replikanın ötesine çıkarmayın, önce
  `pvc-dataprotection-keys.yaml`'daki notu okuyun (ReadWriteMany + paylaşılan key ring
  gerekir, yoksa replikalar birbirinin login cookie'sini çözemez).
- `itinventory-notifier`'ı asla 1'den fazla replika ile çalıştırmayın (her expiration
  e-postası iki kere gider).
