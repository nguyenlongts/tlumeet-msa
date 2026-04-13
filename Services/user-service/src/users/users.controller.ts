// src/users/users.controller.ts
import {
  Controller,
  Get, Patch, Delete,
  Body, Param, Headers,
  ParseIntPipe,
} from '@nestjs/common';
import { EventPattern, Payload } from '@nestjs/microservices';
import { UsersService } from './users.service';

@Controller('api/users')
export class UsersController {
  constructor(private usersService: UsersService) {}


  @Get()
  findAll() {
    return this.usersService.findAll();
  }

  @Get(':userId')
  getProfile(@Param('userId',ParseIntPipe) userId:number){
    return this.usersService.getProfile(userId)
  }

  @Patch(':userId')
  updateProfile(
    @Param('userId') userId:number,
    @Body() body:{
      name?: string;
      phone?: string;
      address?: string;
      avatarUrl?: string;
    }
  ){
    return this.usersService.updateProfile(userId,body)
  }

  @Delete(':id')
  remove(@Param('id') id: string) {
    return this.usersService.remove(id);
  }

  // ── Kafka consumers — nhận event từ .NET ──────────────

  @EventPattern('user-registered-events')
  handleUserRegistered(@Payload() data: {
    UserId: number;
    UserName: string;
    Email: string;
    RegisteredAt:string
  }) {
    return this.usersService.createUserFromEvent(data)
  }

}