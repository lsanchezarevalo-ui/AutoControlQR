import { Router } from 'express';
import { PrismaClient } from '@prisma/client';

const prisma = new PrismaClient();
const router = Router();

// Create a reading from public QR form
router.post('/', async (req, res) => {
  try {
    const { vehicleId, odometerKm, name, note } = req.body;
    if (!vehicleId || typeof odometerKm !== 'number') {
      return res.status(400).json({ error: 'vehicleId and odometerKm are required' });
    }
    // simple validation: get last reading
    const last = await prisma.reading.findFirst({
      where: { vehicleId },
      orderBy: { createdAt: 'desc' }
    });
    if (last && odometerKm < last.odometerKm) {
      return res.status(400).json({ error: 'Odometer cannot decrease' });
    }
    const reading = await prisma.reading.create({
      data: {
        vehicle: { connect: { id: vehicleId } },
        reporterName: name || null,
        odometerKm,
        note: note || null
      }
    });
    return res.status(201).json(reading);
  } catch (err) {
    console.error(err);
    return res.status(500).json({ error: 'server_error' });
  }
});

export default router;
