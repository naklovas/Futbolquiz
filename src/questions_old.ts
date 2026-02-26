import { Question } from './types';

export const QUESTIONS: Question[] = [
  // Seviye 1: 90'lar ve 2000'lerin Başı (Milli Takımlar)
  { id: '1', text: '1994 Dünya Kupası finalinde İtalya adına son penaltıyı kaçırarak kupayı Brezilya\'ya kaptıran efsane kimdir?', options: ['Franco Baresi', 'Daniele Massaro', 'Roberto Baggio', 'Gianluca Vialli'], correctAnswer: 'Roberto Baggio', category: '90\'lar' },
  { id: '2', text: 'EURO 2004 finalinde ev sahibi Portekiz\'i yenerek futbol tarihinin en büyük sürprizlerinden birini yapan ülke hangisidir?', options: ['Çek Cumhuriyeti', 'Yunanistan', 'Danimarka', 'Rusya'], correctAnswer: 'Yunanistan', category: '2000\'ler' },
  { id: '3', text: '1998 Dünya Kupası finalinde Fransa, Brezilya\'yı 3-0 yenerken Zinedine Zidane kaç gol atmıştır?', options: ['1', '2', '3', '0'], correctAnswer: '2', category: '90\'lar' },
  { id: '4', text: '2002 Dünya Kupası\'nda Güney Kore ve Japonya\'da şampiyon olan Brezilya\'nın kalesini kim koruyordu?', options: ['Dida', 'Julio Cesar', 'Marcos', 'Taffarel'], correctAnswer: 'Marcos', category: '2000\'ler' },
  { id: '5', text: 'EURO 96\'da İngiltere\'yi yarı finalde penaltılarla eleyen Almanya\'nın o günkü kalecisi kimdir?', options: ['Oliver Kahn', 'Andreas Köpke', 'Jens Lehmann', 'Bodo Illgner'], correctAnswer: 'Andreas Köpke', category: '90\'lar' },

  // Seviye 2: Şampiyonlar Ligi Modern Dönem (1992+)
  { id: '6', text: '1999 Şampiyonlar Ligi finalinde Manchester United, Bayern Münih\'i uzatmalarda yenerken galibiyet golünü kim atmıştır?', options: ['Teddy Sheringham', 'Ole Gunnar Solskjaer', 'David Beckham', 'Ryan Giggs'], correctAnswer: 'Ole Gunnar Solskjaer', category: 'Şampiyonlar Ligi' },
  { id: '7', text: '2005 "İstanbul Mucizesi"nde Liverpool, Milan karşısında ilk yarıyı kaç kaç geride kapatmıştır?', options: ['2-0', '3-0', '3-1', '4-0'], correctAnswer: '3-0', category: 'Şampiyonlar Ligi' },
  { id: '8', text: 'Şampiyonlar Ligi tarihinde üst üste 3 kez şampiyon olan (2016-2018) tek teknik direktör kimdir?', options: ['Pep Guardiola', 'Jose Mourinho', 'Zinedine Zidane', 'Carlo Ancelotti'], correctAnswer: 'Zinedine Zidane', category: 'Şampiyonlar Ligi' },
  { id: '9', text: '2004 yılında Porto ile Şampiyonlar Ligi\'ni kazanan Jose Mourinho, finalde hangi takımı yenmiştir?', options: ['Monaco', 'Lyon', 'Deportivo', 'Milan'], correctAnswer: 'Monaco', category: 'Şampiyonlar Ligi' },
  { id: '10', text: 'Şampiyonlar Ligi tarihinde bir sezonda en çok gol atan oyuncu kimdir? (17 gol)', options: ['Lionel Messi', 'Robert Lewandowski', 'Cristiano Ronaldo', 'Erling Haaland'], correctAnswer: 'Cristiano Ronaldo', category: 'Rekorlar' },

  // Seviye 3: Modern Efsaneler ve Ballon d'Or
  { id: '11', text: 'Lionel Messi ve Cristiano Ronaldo\'nun Ballon d\'Or serisini 2018 yılında bozan ilk futbolcu kimdir?', options: ['Neymar', 'Luka Modric', 'Antoine Griezmann', 'Virgil van Dijk'], correctAnswer: 'Luka Modric', category: 'Ödüller' },
  { id: '12', text: 'Ronaldinho, Barcelona formasıyla hangi maçtaki performansından sonra rakip taraftarlar (Real Madrid) tarafından ayakta alkışlanmıştır?', options: ['El Clasico (2005)', 'Kral Kupası (2004)', 'Şampiyonlar Ligi (2006)', 'Süper Kupa (2005)'], correctAnswer: 'El Clasico (2005)', category: 'Efsaneler' },
  { id: '13', text: 'Thierry Henry, Arsenal formasıyla Premier Lig\'de kaç kez gol kralı (Altın Ayakkabı) olmuştur?', options: ['2', '3', '4', '5'], correctAnswer: '4', category: 'Premier Lig' },
  { id: '14', text: 'Zlatan Ibrahimovic, 2012 yılında İsveç formasıyla İngiltere\'ye yaklaşık kaç metreden röveşata golü atmıştır?', options: ['25', '30', '33', '40'], correctAnswer: '33', category: 'Efsaneler' },
  { id: '15', text: 'Andres Iniesta, 2010 Dünya Kupası finalinde Hollanda\'ya kaçıncı dakikada galibiyet golünü atmıştır?', options: ['90', '105', '116', '120'], correctAnswer: '116', category: 'Dünya Kupası' },

  // Seviye 4: Yakın Dönem Dünya Kupaları (2010-2022)
  { id: '16', text: '2014 Dünya Kupası yarı finalinde Almanya, Brezilya\'yı 7-1 yenerken ilk 30 dakikada kaç gol atmıştır?', options: ['3', '4', '5', '6'], correctAnswer: '5', category: 'Dünya Kupası' },
  { id: '17', text: '2018 Dünya Kupası\'nda "En İyi Genç Oyuncu" ödülünü kim kazanmıştır?', options: ['Kylian Mbappe', 'Luka Modric', 'Harry Kane', 'Paul Pogba'], correctAnswer: 'Kylian Mbappe', category: 'Dünya Kupası' },
  { id: '18', text: '2022 Dünya Kupası\'nda Fas, çeyrek finalde hangi Avrupa devini eleyerek yarı finale çıkan ilk Afrika takımı olmuştur?', options: ['İspanya', 'Portekiz', 'Belçika', 'Fransa'], correctAnswer: 'Portekiz', category: 'Dünya Kupası' },
  { id: '19', text: '2010 Dünya Kupası\'nın resmi topunun adı nedir? (Çok eleştirilen top)', options: ['Jabulani', 'Brazuca', 'Telstar', 'Al Rihla'], correctAnswer: 'Jabulani', category: 'Dünya Kupası' },
  { id: '20', text: 'Hangi futbolcu 2010, 2014 ve 2018 Dünya Kupalarında gol atma başarısı göstermiştir?', options: ['Cristiano Ronaldo', 'Lionel Messi', 'Luis Suarez', 'Hepsi'], correctAnswer: 'Hepsi', category: 'Dünya Kupası' },

  // Seviye 5: Premier Lig ve Avrupa Ligleri (Yakın Tarih)
  { id: '21', text: 'Manchester City, 2012 yılında QPR\'ı yenerek şampiyon olduğunda Sergio Agüero galibiyet golünü kaçıncı dakikada atmıştır?', options: ['90+1', '90+2', '90+4', '90+5'], correctAnswer: '90+4', category: 'Premier Lig' },
  { id: '22', text: 'Leicester City 2016\'da şampiyon olduğunda, sezon başında şampiyonluk bahis oranı kaçtı?', options: ['1\'e 500', '1\'e 1000', '1\'e 5000', '1\'e 10000'], correctAnswer: '1\'e 5000', category: 'Premier Lig' },
  { id: '23', text: 'Robert Lewandowski, 2015 yılında Wolfsburg maçında oyuna sonradan girip 9 dakikada kaç gol atmıştır?', options: ['3', '4', '5', '6'], correctAnswer: '5', category: 'Bundesliga' },
  { id: '24', text: 'Hangi takım 2011-2020 yılları arasında İtalya Serie A\'da üst üste 9 kez şampiyon olmuştur?', options: ['Inter', 'Milan', 'Juventus', 'Napoli'], correctAnswer: 'Juventus', category: 'Serie A' },
  { id: '25', text: 'Atletico Madrid, 2014 yılında La Liga şampiyonluğunu hangi stadyumda ilan etmiştir?', options: ['Vicente Calderon', 'Santiago Bernabeu', 'Camp Nou', 'Mestalla'], correctAnswer: 'Camp Nou', category: 'La Liga' },

  // Seviye 6: Modern Teknik Direktörler ve Taktikler
  { id: '26', text: 'Pep Guardiola\'nın Barcelona\'da uyguladığı ve kısa paslara dayalı oyun felsefesine ne ad verilir?', options: ['Catenaccio', 'Tiki-Taka', 'Gegenpressing', 'Total Futbol'], correctAnswer: 'Tiki-Taka', category: 'Taktik' },
  { id: '27', text: 'Jurgen Klopp ile özdeşleşen ve top kaybedildiği an yapılan yoğun baskı sisteminin adı nedir?', options: ['Park the Bus', 'Gegenpressing', 'False Nine', 'Zonal Marking'], correctAnswer: 'Gegenpressing', category: 'Taktik' },
  { id: '28', text: 'Hangi teknik direktör "The Special One" lakabıyla tanınır?', options: ['Pep Guardiola', 'Jose Mourinho', 'Arsene Wenger', 'Carlo Ancelotti'], correctAnswer: 'Jose Mourinho', category: 'Teknik Direktörler' },
  { id: '29', text: 'Sir Alex Ferguson, Manchester United başındaki son maçında hangi takımla 5-5 berabere kalmıştır?', options: ['West Bromwich', 'Everton', 'Swansea', 'Chelsea'], correctAnswer: 'West Bromwich', category: 'Teknik Direktörler' },
  { id: '30', text: '2023 yılında Manchester City ile "Treble" (3 kupa) yapan teknik direktör kimdir?', options: ['Thomas Tuchel', 'Pep Guardiola', 'Erik ten Hag', 'Mikel Arteta'], correctAnswer: 'Pep Guardiola', category: 'Teknik Direktörler' },

  // Seviye 7: Modern Rekorlar ve Transferler
  { id: '31', text: 'Futbol tarihinin en pahalı transferi (222 Milyon Euro) olan Neymar, hangi takımdan PSG\'ye geçmiştir?', options: ['Santos', 'Barcelona', 'Real Madrid', 'Milan'], correctAnswer: 'Barcelona', category: 'Transferler' },
  { id: '32', text: 'Erling Haaland, Premier Lig\'deki ilk sezonunda (2022-23) kaç gol atarak rekor kırmıştır?', options: ['32', '34', '36', '38'], correctAnswer: '36', category: 'Rekorlar' },
  { id: '33', text: 'Hangi kaleci Şampiyonlar Ligi finalinde iki farklı takımla (Real Madrid ve Porto) şampiyonluk yaşamıştır?', options: ['Iker Casillas', 'Keylor Navas', 'Edwin van der Sar', 'Manuel Neuer'], correctAnswer: 'Iker Casillas', category: 'Oyuncular' },
  { id: '34', text: 'Lionel Messi, 2021 yılında Barcelona\'dan ayrıldıktan sonra hangi takıma transfer olmuştur?', options: ['Inter Miami', 'PSG', 'Manchester City', 'Al Hilal'], correctAnswer: 'PSG', category: 'Transferler' },
  { id: '35', text: 'Cristiano Ronaldo, kariyerindeki 800. resmi golünü hangi takım formasıyla atmıştır?', options: ['Juventus', 'Real Madrid', 'Manchester United', 'Al Nassr'], correctAnswer: 'Manchester United', category: 'Rekorlar' },

  // Seviye 8: Türkiye Futbolu (2000 Sonrası)
  { id: '36', text: 'Türkiye Milli Takımı, EURO 2008 çeyrek finalinde Hırvatistan\'ı penaltılarla elerken 120+2\'de beraberlik golünü kim atmıştır?', options: ['Nihat Kahveci', 'Semih Şentürk', 'Arda Turan', 'Tuncay Şanlı'], correctAnswer: 'Semih Şentürk', category: 'Türkiye' },
  { id: '37', text: 'Galatasaray\'ın 2012-13 sezonunda Şampiyonlar Ligi çeyrek finalinde Real Madrid\'i 3-2 yendiği maçta gol atan yıldızlar kimlerdi?', options: ['Eboue, Sneijder, Drogba', 'Burak, Selçuk, Elmander', 'Melo, Amrabat, Riera', 'Muslera, Sabri, Hamit'], correctAnswer: 'Eboue, Sneijder, Drogba', category: 'Türkiye' },
  { id: '38', text: 'Beşiktaş, 2017-18 Şampiyonlar Ligi grubunu kaç puanla namağlup lider tamamlayarak rekor kırmıştır?', options: ['12', '14', '15', '16'], correctAnswer: '14', category: 'Türkiye' },
  { id: '39', text: 'Fenerbahçe\'nin 2007-08 Şampiyonlar Ligi çeyrek finalindeki teknik direktörü kimdir?', options: ['Christoph Daum', 'Zico', 'Luis Aragones', 'Aykut Kocaman'], correctAnswer: 'Zico', category: 'Türkiye' },
  { id: '40', text: 'Arda Güler, Real Madrid formasıyla ilk golünü hangi takıma atmıştır?', options: ['Celta Vigo', 'Getafe', 'Granada', 'Alaves'], correctAnswer: 'Celta Vigo', category: 'Türkiye' },

  // Seviye 9: Güncel Olaylar (2020-2025)
  { id: '41', text: 'EURO 2024 finalinde İspanya, İngiltere\'yi kaç kaç yenerek şampiyon olmuştur?', options: ['1-0', '2-1', '3-1', '2-0'], correctAnswer: '2-1', category: 'Güncel' },
  { id: '42', text: 'Hangi futbolcu 2024 yılında Ballon d\'Or ödülünü kazanarak büyük bir tartışma yaratmıştır?', options: ['Vinicius Jr', 'Rodri', 'Jude Bellingham', 'Erling Haaland'], correctAnswer: 'Rodri', category: 'Güncel' },
  { id: '43', text: 'Bayer Leverkusen, 2023-24 sezonunda Bundesliga\'da kaç maçlık namağlup seri yakalamıştır? (Tüm kulvarlar)', options: ['45', '48', '51', '55'], correctAnswer: '51', category: 'Güncel' },
  { id: '44', text: '2022 Dünya Kupası finalinde Arjantin kalecisi Emiliano Martinez, son dakikada hangi Fransız oyuncunun şutunu kurtararak maçı penaltılara taşımıştır?', options: ['Kolo Muani', 'Kingsley Coman', 'Marcus Thuram', 'Olivier Giroud'], correctAnswer: 'Kolo Muani', category: 'Güncel' },
  { id: '45', text: 'Harry Kane, 2023 yazında Tottenham\'dan hangi takıma transfer olmuştur?', options: ['Manchester City', 'Real Madrid', 'Bayern Münih', 'PSG'], correctAnswer: 'Bayern Münih', category: 'Güncel' },

  // Seviye 10: Karışık Zor Modern Sorular
  { id: '46', text: 'Şampiyonlar Ligi tarihinde bir maçta 5 gol atan ilk oyuncu kimdir?', options: ['Lionel Messi', 'Luiz Adriano', 'Erling Haaland', 'Cristiano Ronaldo'], correctAnswer: 'Lionel Messi', category: 'Rekorlar' },
  { id: '47', text: 'Hangi futbolcu hem 20. yüzyılda hem de 21. yüzyılda Dünya Kupası finalinde gol atmıştır?', options: ['Pele', 'Ronaldo Nazario', 'Zinedine Zidane', 'Lionel Messi'], correctAnswer: 'Zinedine Zidane', category: 'Rekorlar' },
  { id: '48', text: '2019 Şampiyonlar Ligi yarı finalinde Liverpool, Barcelona\'yı 4-0 yenerken köşe vuruşunu hızlıca kullanan oyuncu kimdir?', options: ['Trent Alexander-Arnold', 'Andrew Robertson', 'Jordan Henderson', 'James Milner'], correctAnswer: 'Trent Alexander-Arnold', category: 'Şampiyonlar Ligi' },
  { id: '49', text: 'Hangi ülke 2021 yılında düzenlenen EURO 2020\'de tüm maçlarını kendi evinde oynamadan şampiyon olmuştur?', options: ['İngiltere', 'İtalya', 'İspanya', 'Danimarka'], correctAnswer: 'İtalya', category: 'EURO' },
  { id: '50', text: 'Cristiano Ronaldo, Real Madrid formasıyla çıktığı 438 resmi maçta toplam kaç gol atmıştır?', options: ['438', '450', '462', '420'], correctAnswer: '450', category: 'Rekorlar' },
];
