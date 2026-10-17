-- DeltaFlow · İşlev haritası: AI sonuçları ERT tablosunda tutulur.
-- AI_KUME : sunucunun işlev kümesi (ör. "ETL / Veri entegrasyonu")
-- AI_DETAY: JSON (işlev, rol, güven, gerekçe, küme açıklaması, tarih, kaynak "AI" ya da "Elle: kullanıcı")
IF COL_LENGTH('dbo.ERT_HOSTIPADDRESS', 'AI_KUME') IS NULL
    ALTER TABLE dbo.ERT_HOSTIPADDRESS ADD AI_KUME NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.ERT_HOSTIPADDRESS', 'AI_DETAY') IS NULL
    ALTER TABLE dbo.ERT_HOSTIPADDRESS ADD AI_DETAY NVARCHAR(MAX) NULL;
GO
-- Uygulama havuzu hesabı ERT'ye zaten UPDATE yetkisine sahipse (TAG için verildi) bu adım gerekmez.
-- Okuma yetkisi: MSY / ÇSY / Kahin / SM tabloları
-- GRANT SELECT ON dbo.Envanter TO [DOMAIN\DeltaFlowServisHesabi];
-- GRANT SELECT ON dbo.CsyEnvanter TO [DOMAIN\DeltaFlowServisHesabi];
-- GRANT SELECT ON dbo.SONUCLAR TO [DOMAIN\DeltaFlowServisHesabi];
-- GRANT SELECT ON dbo.REQUESTM1 TO [DOMAIN\DeltaFlowServisHesabi];
-- GRANT SELECT ON dbo.DEPTM1 TO [DOMAIN\DeltaFlowServisHesabi];
-- GRANT SELECT ON dbo.CM3RM1 TO [DOMAIN\DeltaFlowServisHesabi];
-- GRANT SELECT ON dbo.ZTSUNUCUKURULUMUOPTIONSM1 TO [DOMAIN\DeltaFlowServisHesabi];
