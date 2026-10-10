using AutoMapper;
using BlotterSync.DTOs;
using BlotterSync.Models;
using static BlotterSync.DTOs.BlotterRecordDTOs;
using static BlotterSync.DTOs.ResidentDTOs;

namespace BlotterSync.Profiles
{
    public class BlotterProfile : Profile
    {
        public BlotterProfile()
        {
            CreateMap<BlotterRecord, BlotterRecordDTO>();
            CreateMap<CreateBlotterRecordDTO, BlotterRecord>();
            CreateMap<UpdateBlotterRecordDTO, BlotterRecord>();
            CreateMap<Resident, ResidentDTO>();
            CreateMap<CreateResidentDTO, Resident>();
        }
    }
}