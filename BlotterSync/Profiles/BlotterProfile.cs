using AutoMapper;
using BlotterSync.DTOs;
using BlotterSync.Models;
using static BlotterSync.DTOs.BlotterRecordDTOs;

namespace BlotterSync.Profiles
{
    public class BlotterProfile : Profile
    {
        public BlotterProfile()
        {
            CreateMap<BlotterRecord, BlotterRecordDTO>();

            CreateMap<CreateBlotterRecordDTO, BlotterRecord>();

            CreateMap<UpdateBlotterRecordDTO, BlotterRecord>();
        }
    }
}