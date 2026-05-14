import { Injectable, Inject, NotFoundException } from '@nestjs/common';
import { InjectModel } from '@nestjs/mongoose';
import { Model } from 'mongoose';
import { ClientKafka } from '@nestjs/microservices';
import { UserStat, UserStatDocument } from '../schemas/user-stat.schema';
import {
  MeetingStat,
  MeetingStatDocument,
} from '../schemas/meeting-stat.schema';

@Injectable()
export class AdminService {
  constructor(
    @InjectModel(UserStat.name) private userStatModel: Model<UserStatDocument>,
    @InjectModel(MeetingStat.name)
    private meetingStatModel: Model<MeetingStatDocument>,
    @Inject('KAFKA_PRODUCER') private kafkaClient: ClientKafka,
  ) {}

  async getStats() {
    const [totalUsers, totalMeetings, activeMeetings] = await Promise.all([
      this.userStatModel.countDocuments(),
      this.meetingStatModel.countDocuments(),
      this.meetingStatModel.countDocuments({ status: 'live' }),
    ]);

    return { totalUsers, totalMeetings, activeMeetings };
  }

  async getUsers(page: number = 1, limit: number = 5) {
    const skip = (page - 1) * limit;

    const [users, total] = await Promise.all([
      this.userStatModel
        .find()
        .sort({ registeredAt: -1 })
        .skip(skip)
        .limit(limit),
      this.userStatModel.countDocuments(),
    ]);

    return {
      data: users,
      pagination: {
        total,
        page,
        limit,
        totalPages: Math.ceil(total / limit),
        hasNext: page < Math.ceil(total / limit),
        hasPrev: page > 1,
      },
    };
  }

  async getMeetings() {
    return this.meetingStatModel.find().sort({ createdAt: -1 });
  }

  async deleteUser(userId: number) {
    const user = await this.userStatModel.findOne({ userId });
    if (!user) {
      throw new NotFoundException(`Không tìm thấy user với id ${userId}`);
    }

    await this.kafkaClient
      .emit('user-deleted-events', {
        UserId: userId,
        Email: user.email,
        DeletedAt: new Date().toISOString(),
      })
      .toPromise();

    await this.userStatModel.deleteOne({ userId });

    return { message: `Đã xóa user ${userId}` };
  }

  async handleUserRegistered(data: {
    UserId: number;
    UserName: string;
    Email: string;
    RegisteredAt: string;
  }) {
    const exists = await this.userStatModel.findOne({ userId: data.UserId });
    if (exists) return;

    await this.userStatModel.create({
      userId: data.UserId,
      userName: data.UserName,
      email: data.Email.toLowerCase(),
      registeredAt: new Date(data.RegisteredAt),
    });
  }

  async handleMeetingCreated(data: {
    MeetingId: number;
    Title: string;
    RoomCode: string;
    HostEmail: string;
    CreatedAt: string;
  }) {
    const exists = await this.meetingStatModel.findOne({
      meetingId: data.MeetingId,
    });
    if (exists) return;

    await this.meetingStatModel.create({
      meetingId: data.MeetingId,
      title: data.Title,
      roomCode: data.RoomCode,
      hostEmail: data.HostEmail,
      createdAt: new Date(data.CreatedAt),
    });
  }

  async handleMeetingStarted(data: {
    MeetingId: number;
    RoomCode: string;
    HostEmail: string;
    StartedAt: string;
  }) {
    await this.meetingStatModel.findOneAndUpdate(
      { meetingId: data.MeetingId },
      { status: 'started', startedAt: new Date(data.StartedAt) },
    );
  }

  async handleMeetingEnded(data: {
    MeetingId: number;
    RoomCode: string;
    HostEmail: string;
    EndedAt: string;
  }) {
    await this.meetingStatModel.findOneAndUpdate(
      { meetingId: data.MeetingId },
      { status: 'ended', endedAt: new Date(data.EndedAt) },
    );
  }

  async handleMeetingDeleted(data: {
    MeetingId: number;
    RoomCode: string;
    HostEmail: string;
    DeletedAt: string;
  }) {
    // await this.meetingStatModel.findOneAndUpdate(
    //   { meetingId: data.MeetingId },
    //   { status: 'deleted', deletedAt: new Date(data.DeletedAt) },
    // );
    await this.meetingStatModel.deleteOne({ meetingId: data.MeetingId });
  }

  async handleParticipantJoined(data: { MeetingId: number }) {
    await this.meetingStatModel.findOneAndUpdate(
      { meetingId: data.MeetingId },
      { $inc: { totalParticipants: 1 } },
    );
  }

  async handleParticipantLeft(data: { MeetingId: number }) {
    await this.meetingStatModel.findOneAndUpdate(
      { meetingId: data.MeetingId },
      { $inc: { totalParticipants: -1 } },
    );
  }

  async handleUserUpdated(data: {
    userId: number;
    updatedFields: {
      userName?: string;
      phone?: string;
      address?: string;
      avatarUrl?: string;
    };
    updatedAt: string;
  }) {
    const { userId, updatedFields, updatedAt } = data;
    const res=await this.userStatModel.findOneAndUpdate(
      { userId },
      { $set: { ...updatedFields, updatedAt } },
      { upsert: true, returnDocument: 'after' },
    );
  }
}
